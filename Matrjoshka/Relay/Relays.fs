namespace Matrjoshka

open System
open System.IO
open System.Threading
open System.Net
open System.Net.Sockets
open Nessos.FsPickler
open System.Collections.Concurrent
open Matrjoshka.Cryptography


/// <summary>
/// RelayInstance represents an instance of a relay communicating with a specific client.
/// Relay instantiates and disposes those instances for each incoming TCP-connection.
/// The RelayInstance is created using a CancellationToken (for shutdown), a name (for debugging/logging),
/// a concrete TcpClient, a RSA-instance (for decrypting incoming connection attempts) and a function
/// removing itself from (possibly) existing caches upon shutdown.
/// </summary>
type RelayInstance(token : CancellationToken, name : string, client : TcpClient, rsa : Rsa, remove : unit -> unit) =
    static let pickler = FsPickler.CreateBinary(true)
    
    let cancelClients = new CancellationTokenSource()
    let Log = logger (sprintf "%s/%A" name client.Client.RemoteEndPoint)
    let forwardClients = ConcurrentDictionary<string * int, PlainSocket>()

    let stream = client.GetStream()
    let reader = new StreamReader(stream)
    let writer = new StreamWriter(stream)
    let mutable aes = None
    let mutable registration : Option<CancellationTokenRegistration> = None

    let shutdown() =
        try
            Log.info "shutdown"
            cancelClients.Cancel()

            for (KeyValue(_,c)) in forwardClients do
                c.Disconnect()

            forwardClients.Clear()

            match registration with
                | Some r -> r.Dispose()
                | None -> ()

            reader.Dispose()
            writer.Dispose()
            client.Close()

            remove()
        with e ->
            Log.error "error in shutdown: %A" e

    do registration <- Some <| token.Register(fun () -> shutdown())

    let receiveAsync() : Async<'a> =
        async {

            let! data = reader.ReadLineAsync() |> Async.AwaitTask

            if data = null then
                return raise <| OperationCanceledException()
            else
                let arr = Convert.FromBase64String(data)

                let arr = 
                    match aes with
                        | Some aes -> Aes.decrypt aes arr
                        | None -> arr

                if typeof<'a> = typeof<byte[]> then
                    return arr :> obj |> unbox
                else
                    return pickler.UnPickle(arr)
        }

    let send (m : 'a) : unit =
        let arr =
            match m :> obj with
                | :? array<byte> as arr -> arr
                | _ -> pickler.Pickle(m)

        let arr =
            match aes with
                | Some aes -> Aes.encrypt aes arr
                | None -> arr

        writer.WriteLine(Convert.ToBase64String arr)
        writer.Flush()

    let forward (remote : string) (port : int) =
        let isNew = ref false
        let client = 
            forwardClients.GetOrAdd((remote,port), fun _->
                isNew := true
                PlainSocket()
            )

        if !isNew then
            client.Connect(remote,port)

            let listenTask =
                async {
                    while true do
                        let! (reply : byte[]) = client.Receive()

                        send reply
                }


            Async.StartAsTask(listenTask, cancellationToken = cancelClients.Token) |> ignore
        
        client


    let runAsync() =
        async {
            try
                Log.debug "running"
                while true do
                    // wait for messages to arrive
                    let! (message : Message) = receiveAsync()

                    //printfn "%s got: %A" name message

                    match message with
                        | Connect(encryptedDh) ->
                            try
                                Log.info "got connection request"
                                // when the client is trying to connect decrypt the client's half
                                // of the Diffie Hellman handshake using the server's private RSA-Key
                                let dhKey = Rsa.decrypt rsa encryptedDh

                                // create a local DiffieHellman-Instance in order to
                                // derive a symmetric key
                                let dh = DiffieHellman.create()
                                let symmetricKey = DiffieHellman.deriveKey dh dhKey
                                
                                // compute a hash for the generated symmetric key which
                                // can be used by the client for validation
                                let hash = Sha.hash symmetricKey

                                // create an Aes-Instance using the generated key
                                let a = Aes.createNew symmetricKey

                                // get the (newly) generated initial vector from the
                                // AES-Instance
                                let iv = Aes.initialVector a

                                // tell the client that the connection has been accepted, 
                                // provide it with our half of the Diffie Hellman handshake,
                                // the initial vector and the key-hash.
                                send (Accept(DiffieHellman.publicKey dh, iv, hash))

                                DiffieHellman.destroy dh

                                // finally store the create AES-instance in order to
                                // encrypt all successive communication
                                aes <- Some a

                            with e ->
                                Log.warn "error during key-handshake: %A" e
                                // if an Exception (of any kind) occurs while connecting
                                // tell the client that the connection has been refused.
                                send (Exception(e.ToString()))


                        | Forward(target,port,data) ->
                            Log.info "forwarding to: %s:%d" target port
                            let client = forward target port
                            client.Send(data)

                        | Request(target, port, data) ->
                            let req = System.Net.HttpWebRequest.Create(target)

                            use response = req.GetResponse()
                            use reader = new StreamReader(response.GetResponseStream())

                            let response = reader.ReadToEnd()
                            
                            let result = System.Text.ASCIIEncoding.UTF8.GetBytes(response)

                            send (Data result)


                        | Command(cmd, obj) ->
                            Log.info "got command: %s(%A)" cmd obj
                            

                    ()
            with 
                | :? ObjectDisposedException | :? IOException | :? OperationCanceledException ->
                    
                    shutdown ()
                | e ->
                    Log.error "unexpected error: %A" e 
            
        }

    member x.Run() =
        Async.RunSynchronously(runAsync())

    member x.Start() =
        Async.StartAsTask(runAsync(), cancellationToken = token) |> ignore

/// <summary>
/// Relay runs at a specific port (port), has a name for debugging purposes
/// and knows the address/port of the Directory-node since it needs to send
/// 'alive' messages to the directory.
/// </summary>
type Relay(directory : string, dirPort : int, name : string, port : int) =
    static let pickler = FsPickler.CreateBinary(true)

    let cancel = new CancellationTokenSource()
    let instances = ConcurrentDictionary<TcpClient, RelayInstance>()
    let listener = TcpListener(IPAddress.Any, port)
    let rsa = Rsa.create()
    let Log = logger name

    let spawn (client : TcpClient) =
        instances.GetOrAdd(client, fun client ->
            let instance = RelayInstance(cancel.Token, name, client, rsa, fun () -> instances.TryRemove client |> ignore)
            instance.Start()
            instance
        ) |>  ignore

    let runAsync() =
        
        async {
            
            try
                listener.Start()
                Log.debug "waiting for connections"

                while true do
                    let! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask
                    client.NoDelay <- true

                    let remote = client.Client.RemoteEndPoint
                    Log.info "got connection from: %A" remote

                    System.Threading.Tasks.Task.Factory.StartNew(fun () -> spawn client) |> ignore
            with 
                | :? OperationCanceledException | :? ObjectDisposedException | :? IOException ->
                    Log.info "shutdown"
                    listener.Stop()
                | e ->
                    Log.error "unexpected error: %A" e
        }

    let pingTimer =
        let self = Dns.GetHostEntry(Dns.GetHostName())
        let address = self.AddressList |> Array.filter (fun a -> a.AddressFamily = AddressFamily.InterNetwork) |> Seq.head

        let udp = new UdpClient()
        let callback (state : obj) =
            let msg = Alive(address.ToString(), port, Rsa.publicKey rsa)
            
            let data = pickler.Pickle msg
            udp.Send(data, data.Length, directory, dirPort) |> ignore

        let interval = System.Random().Next(500) + 1000
        let t = new Timer(TimerCallback(callback), null, 0, interval)
        { new IDisposable with
            member x.Dispose() = 
                t.Dispose()
                let data = pickler.Pickle(Shutdown(address.ToString(), port))
                udp.Send(data, data.Length, directory, dirPort) |> ignore
                udp.Close()
        }


    member x.PublicKey =
        Rsa.publicKey rsa

    member x.Run() =
        runAsync() |> Async.RunSynchronously

    member x.Start() =
        Async.StartAsTask(runAsync(), cancellationToken = cancel.Token) |> ignore

    member x.Stop() =
        Log.info "shutdown"
        pingTimer.Dispose()
        cancel.Cancel()
        Rsa.destroy rsa
        instances.Clear()

