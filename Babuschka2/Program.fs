open System
open System.Net
open System.IO
open System.Net.Sockets
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks

open System.Security.Cryptography

type RSAPublicKey = RSAParameters


[<Serializable>]
type Message = interface end

[<Serializable>]
type Command(cmd : string, data : obj) =
    interface Message
    member x.Command = cmd
    member x.Data = data

    override x.ToString() = sprintf "Command(%A, %A)" cmd data

[<Serializable>]
type Forward(target : string, port : int, pub : RSAPublicKey, data : byte[]) =
    interface Message
    member x.Target = target   
    member x.Port = port
    member x.PublicKey = pub
    member x.Data = data

    override x.ToString() = sprintf "Forward(%A, %A, %A, %A)" target port pub data

[<Serializable>]
type CreateCell(data : byte[]) =
    interface Message
    member x.Data = data

    override x.ToString() = sprintf "CreateCell(%A)" data


[<AutoOpen>]
module MessagePatterns =
    let (|Command|Forward|CreateCell|) (m : Message) =
        match m with
            | :? Command as m -> Command(m.Command, m.Data)
            | :? Forward as m -> Forward(m.Target, m.Port, m.PublicKey, m.Data)
            | :? CreateCell as m -> CreateCell(m.Data)
            | _ -> failwith ""

type Reply = CreateCellReply of ECDiffieHellmanPublicKey * byte[] * byte[]

type Circuit = { id : Guid; dh : ECDiffieHellman; key : byte[] }

type Socket(remote : string, port : int) =
    let c = new TcpClient(remote, port, NoDelay = true)
    let formatter = BinaryFormatter()
    let stream = c.GetStream()
    //let dh = ECDiffieHellmanCng.Create()

    let hash = SHA512.Create()

    let mutable chain = []

    let createCell (dh : ECDiffieHellman) (publicKey : RSAParameters) (id : Guid) =
        let rsa = new RSACryptoServiceProvider(2048)
        rsa.ImportParameters(publicKey)

        let arr = Array.concat [id.ToByteArray(); dh.PublicKey.ToByteArray()]
        let result = CreateCell (rsa.Encrypt(arr, false))
        rsa.Dispose()
        result


    let encryptWith (aes : Aes) (m : Message) =
        let ms = new MemoryStream()
        let c = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write)
        formatter.Serialize(c, m)

        c.FlushFinalBlock()
        let data = ms.ToArray()
        c.Dispose()
        ms.Dispose()
        data

    let decryptWith (aes : Aes) (data : byte[]) =
        let ms = new MemoryStream(data)
        let c = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read)
        let m = formatter.Deserialize(c)
        c.Dispose()
        ms.Dispose()
        m

    let rec decrypt (chain : list<string * int * RSAPublicKey * Aes>) (data : byte[]) =
        match chain with
            | [] ->
                let ms = new MemoryStream(data)
                let o = formatter.Deserialize(ms)
                ms.Dispose()
                o

            | (t,p,pub,aes)::rest ->
                let d = decryptWith aes data
                match d with
                    | :? array<byte> as arr ->  decrypt rest arr
                    | d -> d

    let serialize (m : Message) =
        let ms = new MemoryStream()
        formatter.Serialize(ms, m)
        let arr = ms.ToArray()
        ms.Dispose()
        arr

    let send(m : Message) =
        match chain with
            | [] ->
                formatter.Serialize(stream, m)
            | _ ->
                let (_,_,_,aes) = chain |> List.rev |> List.head
                let data = encryptWith aes m
                formatter.Serialize(stream, data)

    let receive() =
        let arr = formatter.Deserialize(stream)
        decrypt (chain |> List.rev) (arr |> unbox)

    let extend(a : string) (p : int) (k : RSAPublicKey) =
        let g = Guid.NewGuid()
        let dh = ECDiffieHellmanCng.Create()
        let msg = createCell dh k g

        match chain with
            | [] -> send msg
            | chain ->
                let rec build c =
                    match c with
                        //| [(a,p,k,aes)] -> Forward(a,p,k, serialize msg) :> Message
                        | (a,p,k,aes)::cs ->
                            let m = build cs
                            Forward(a, p, k, encryptWith aes m) :> Message
                        | [] -> Forward(a,p,k, serialize msg) :> Message

                match build (List.rev chain) with
                    | Forward(_,_,_,m) ->
                        formatter.Serialize(stream, m)
                    | _ -> failwith ""


        let reply = receive() |> unbox<Reply>
        match reply with
            | CreateCellReply(pub, iv, h) ->

                let key = dh.DeriveKeyMaterial(pub)
                let h' = hash.ComputeHash(key)

                if h <> h' then
                    failwith "could not create session (got invalid hash from remote)"
                else
                    let aes = AesCryptoServiceProvider.Create()
                    
                    aes.Key <- key
                    aes.IV <- iv
                    //[remote, port, publicKey, ownAes]
                    chain <- (a,p,k,aes)::chain


                        

    member x.Send(m : Message) =
        match chain with
            | [] ->
                formatter.Serialize(stream, m)
            | _ ->

                let final = List.fold (fun m (a,p,k,aes) -> Forward(a,p,k, encryptWith aes m) :> Message) m chain
                match final with
                    | Forward(_,_,_,data) -> formatter.Serialize(stream, data)
                    | _ -> failwith ""

    member x.Request(m : Message) =
        x.Send m
        Task.Factory.StartNew(fun () ->
            x.Receive()
        )

    member x.SendData(data : byte[]) =
        formatter.Serialize(stream, data)

    member x.Receive() =
       receive()

    member x.Extend(a : string, p : int, k : RSAPublicKey) =
        extend a p k

    member x.StarListening(f : byte[] -> unit) =
        Task.Factory.StartNew(fun () ->
            try
                while true do
                    let reply = formatter.Deserialize(stream) |> unbox<byte[]>

                    f reply
            with e ->
                printfn "client listener faulted: %A" e
            ()
        ) |> ignore

    member x.Dispose() =
        c.Client.Dispose()
        
    interface IDisposable with
        member x.Dispose() = x.Dispose()

type RouterInstance(rsa : RSACryptoServiceProvider, name : string, client : TcpClient, remove : unit -> unit) =
    let mutable running = 0
    let formatter = BinaryFormatter()
    let ip = client.Client.RemoteEndPoint
    let log fmt = Printf.kprintf(fun str -> printfn "%s/%A: %s" name ip str) fmt

    let functions = System.Collections.Generic.List<Message -> Option<obj>>()

    let clients = ConcurrentDictionary<string, Socket>()

    let hash = SHA512.Create()

    let mutable circuit = None
    let mutable stream = null
    let mutable aes : Aes = null

    let encryptBytes (data : byte[]) : byte[] =
        let ms = new MemoryStream()
        let c = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write)
        c.Write(data, 0, data.Length)
        c.FlushFinalBlock()
        let arr = ms.ToArray()
        c.Dispose()
        ms.Dispose()
        arr

    let encrypt (r : obj) : byte[] =
        let ms = new MemoryStream()
        let arr =
            if aes = null then
                formatter.Serialize(ms, r)
                let arr = ms.ToArray()
                ms.Dispose()
                arr
            else
                let c = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write)
                formatter.Serialize(c, r)
                c.FlushFinalBlock()
                let arr = ms.ToArray()
                c.Dispose()
                ms.Dispose()
                arr
        arr

    let decrypt (r : byte[]) : obj =
        let ms = new MemoryStream(r)

        let data = 
            if aes = null then
                formatter.Deserialize(ms)
            else
                let c = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read)
                let data = formatter.Deserialize(c)
                c.Dispose()
                data

        ms.Dispose()
        data

    

    let reply(r : Reply) =
        formatter.Serialize(stream, encrypt r)

    let receive() =
        match formatter.Deserialize(stream) with
            | :? array<byte> as arr ->
                decrypt arr
            | o -> o


    let getClient (target : string) (port : int) (pub : RSAPublicKey) =
        let isNew = ref false
        let client = clients.GetOrAdd(target, fun target -> isNew := true; new Socket(target, port))

        if !isNew then
            client.StarListening(fun data ->
                //printfn "client in %A got message from %A:%A" name target port
                formatter.Serialize(stream, encrypt data)
            )

        client

    let createCell data =

        // data is actually: Guid * ECDiffieHellmanPublicKey
        let decrypted = rsa.Decrypt(data, false)

        let guid = Array.sub decrypted 0 16
        let pub = Array.sub decrypted 16 (decrypted.Length - 16)
        let guid = Guid(guid)
        let pub = ECDiffieHellmanCngPublicKey.FromByteArray(pub, CngKeyBlobFormat.GenericPublicBlob)


        let dh = ECDiffieHellmanCng.Create()
        let key = dh.DeriveKeyMaterial pub

        let a = AesCryptoServiceProvider.Create()
        a.Key <- key
        a.GenerateIV()
                                    
        // compute a key-hash so the originator can validate that 
        // the corrent key has been derived
        let hash = hash.ComputeHash(key)

        // reply with the local half of the DH handshake and the key-hash
        reply (CreateCellReply(dh.PublicKey, a.IV, hash))

        aes <- a

        try
            // dispose the old DH handshake (since a new one was established)
            match circuit with
                | Some old -> old.dh.Dispose()
                | None -> ()

            // store the instance's circuit and read/write streams (using the shared secret)
            circuit <- Some { id = guid; dh = dh; key = key}

        with e ->
            log "ERROR: %A" e

    let run() =
        let wasRunning = Interlocked.Exchange(&running, 1)

        if wasRunning = 0 then
            log "instance running"
            stream <- client.GetStream()
            try
                while true do
                    let msg = receive()

                    match msg with
                            
                        | :? Message as msg ->
                            match msg with
                                | CreateCell data ->
                                    createCell data

                                | Forward(target, port, pub, content) ->
                                    
                                    let c = getClient target port pub
                                    c.SendData content

                                | _ -> 

                                    //log "instance got message: %A" msg
                       
                                    for f in functions do
                                        try 
                                            match f msg with
                                                | Some data ->
                                                    formatter.Serialize(stream, encrypt data)
                                                | None ->
                                                    ()
                                        with e ->
                                            ()

                        | _ ->
                            log "instance got unsupported message: %A" msg

            with e ->
                log "instance cancelled:%A" e
                running <- 0
                remove()
    

    member x.Install(fs : seq<Message -> Option<obj>>) =
        functions.AddRange fs

    member x.AddFunction(f : Message -> Option<obj>) =
        functions.Add f

    member x.Run() =
        run()

    member x.Start() =
        stream <- client.GetStream()
        Task.Factory.StartNew(fun () ->
            run()
        ) |> ignore
    
type Router(name : string, port : int) =
    let listener = TcpListener(IPAddress.Any, port)
    let mutable task = null
    let instances = ConcurrentDictionary<EndPoint, RouterInstance>()

    let functions = System.Collections.Generic.List<Message -> Option<obj>>()

    let log fmt = Printf.kprintf(fun str -> printfn "%s: %s" name str) fmt
    let rsa = new RSACryptoServiceProvider(2048)
    let publicKey = rsa.ExportParameters(false)



    let run() =
        try
            log "server running"
            while true do
                let client = listener.AcceptTcpClient()
                client.NoDelay <- true
                let remote = client.Client.RemoteEndPoint
                log "accpeted connection from %A" remote

                let instance = RouterInstance(rsa, name, client, fun () -> instances.TryRemove remote |> ignore)//instances.GetOrAdd(remote, fun remote -> RouterInstance(rsa, name, client, fun () -> instances.TryRemove remote |> ignore))
                instance.Install(functions)


                instance.Start()

        with e ->
            log "server cancelled"

    member x.PublicKey = publicKey

    member x.AddFunction(f : Message -> Option<obj>) =
        functions.Add f
        for (KeyValue(_,i)) in instances do
            i.AddFunction f

    member x.Start() =
        listener.Start()
        task <- Task.Factory.StartNew(fun () -> run())

    member x.Stop() =
        listener.Stop()
        task.Wait()
        task <- null

    member x.Run() =
        listener.Start()
        run()

type Client(chain : list<string * int * RSAPublicKey>) =
    let socket =
        match chain with
            | (r,p,k)::_ -> new Socket(r,p)
            | _ -> failwith "cannot create client with empty chain"

    do for (r,p,k) in chain do
        socket.Extend(r,p,k)

    member x.Send(command : string, data : obj) =
        socket.Send(Command(command, data))

    member x.Request(command : string, data : obj) =
        socket.Request(Command(command, data))
    


[<EntryPoint>]
let main args =
    Babuschka.Test.run()

    Babuschka3.Test.run()
    System.Environment.Exit(0)

    let s0 = Router("s0", 11111)
    s0.Start()

    let s1 = Router("s1", 22222)
    s1.Start()

    let s2 = Router("s2", 33333)
    s2.Start()

    let s3 = Router("s3", 44444)
    s3.Start()


    s3.AddFunction(fun m ->
        match m with
            | Command("GET", data) ->
                match data with
                    | :? string as url ->
                        let r = WebRequest.Create(url) |> unbox<HttpWebRequest>
                        let r = r.GetResponse() |> unbox<HttpWebResponse>

                        let r = new StreamReader(r.GetResponseStream())
                        let str = r.ReadToEnd()
                        r.Dispose()

                        Some (str :>  obj)
                    | _ -> None
            | _ -> None
    )

    s2.AddFunction(fun m ->
        match m with
            | Command("GET", data) ->
                match data with
                    | :? string as url ->
                        let r = WebRequest.Create(url) |> unbox<HttpWebRequest>
                        let r = r.GetResponse() |> unbox<HttpWebResponse>

                        let r = new StreamReader(r.GetResponseStream())
                        let str = r.ReadToEnd()
                        r.Dispose()

                        Some (str :>  obj)
                    | _ -> None
            | _ -> None
    )


    let e = System.Diagnostics.Stopwatch()
    e.Start()
    let client = 
        Client [
            "localhost", 11111, s0.PublicKey
            "localhost", 22222, s1.PublicKey
            "localhost", 33333, s2.PublicKey
            "localhost", 44444, s3.PublicKey
        ]

    e.Stop()

    let e2 = System.Diagnostics.Stopwatch()
    e2.Start()
    let client2 = 
        Client [
            "localhost", 11111, s0.PublicKey
            "localhost", 22222, s1.PublicKey
            "localhost", 33333, s2.PublicKey
        ]
    e2.Stop()

    let get (url) = client.Request("GET", url).Result |> unbox<string>
    let get2 (url) = client2.Request("GET", url).Result |> unbox<string>

    let i = System.Diagnostics.Stopwatch()
    i.Start()
    let result = get "http://www.orf.at"
    i.Stop()

    let google = get "http://www.google.de"

    let f = System.Diagnostics.Stopwatch()
    f.Start()
    let google2 = get "http://www.orf.at"
    f.Stop()
    

    printfn "setup:   %Ams" e.Elapsed.TotalMilliseconds
    printfn "setup 2: %Ams" e.Elapsed.TotalMilliseconds
    printfn "initial: %Ams" i.Elapsed.TotalMilliseconds
    printfn "follow:  %Ams" f.Elapsed.TotalMilliseconds

    Console.ReadLine() |> ignore

    let google2 = get2 "http://www.orf.at"

    0