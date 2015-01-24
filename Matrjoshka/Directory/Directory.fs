namespace Matrjoshka

open System
open System.IO
open System.Collections.Concurrent
open System.Threading
open System.Net
open System.Net.Sockets
open Nessos.FsPickler

type Directory(port : int, pingPort : int, remapAddress : string -> string, serviceAddress : string, servicePort : int) =
    static let pickler = FsPickler.CreateBinary(true)
    let pingListener = new UdpClient(pingPort)
    let listener = new TcpListener(IPAddress.Any, port)
    let cancel = new CancellationTokenSource()
    let random = System.Random()

    let Log = logger "dir"

    let content = ConcurrentDictionary<string * int, byte[] * DateTime * int>()

    let start (a : Async<'a>) =
        Async.StartAsTask(a, cancellationToken = cancel.Token) |> ignore



    let increaseUseCount (a,b) =
        let worked, (c,d,useCount) = content.TryGetValue ((a,b)) 
        printf "worked : %A" worked
        content.TryUpdate ((a,b), (c,d,useCount+1), (c,d,useCount))



    let startPingListener() =
        async {
            Log.info "started"
            while true do
                let! data = pingListener.ReceiveAsync() |> Async.AwaitTask
                let time = DateTime.Now

                try
                    let ping = pickler.UnPickle(data.Buffer)
                    match ping with
                        | Alive(address, port, key) ->
                            let address = remapAddress address
                            let id = (address, port)
                            //Log.info "got alive from: %s:%d" address port


                            let addFun (address, port) =
                                Log.info "chain logged in: %s:%d" address port
                                key, time, 0

                            let updateFun (address, port) (oldKey, oldTime, oldUseCount) =
                                key, time, oldUseCount

                            content.AddOrUpdate(id, addFun, updateFun) |> ignore

                            

                        | Shutdown(address, port) ->

                            content.TryRemove((address, port)) |> ignore
                with e ->
                    Log.warn "received corrupt UDP-Packet: %A" e
        }

    let getAllRelays() =
        let mutable result = []
        let mutable remove = []

        for (KeyValue(k,v)) in content do
                    
            let (address, port) = k
            let (key, time, useCount) = v

            let age = DateTime.Now - time

            if age.TotalSeconds > 4.0 then
                remove <- k::remove
            else
                result <- (address, port, key, useCount)::result

                                
        for r in remove do
            content.TryRemove r |> ignore

        result
      
    let startInstance(client : TcpClient) =
        let Log = logger (sprintf "dir/%A" client.Client.RemoteEndPoint)

        async {
            Log.info "running"
            let stream = client.GetStream()
            let reader = new StreamReader(stream)
            let writer = new StreamWriter(stream)

            try
                while true do
                    let! line = reader.ReadLineAsync() |> Async.AwaitTask
                    if line = null then
                        raise <| OperationCanceledException()

                    let data = Convert.FromBase64String line

                    let request = pickler.UnPickle data
                    let all = getAllRelays()

                    let reply =
                        match request with
                            | All -> Nodes all

                            | Service ->
                                Address(serviceAddress, servicePort)

                            | Random count ->
                                let arr = all |> List.toArray

                                if arr.Length >= count && count < 10 then
                                    let mutable set = Set.empty
                                    while Set.count set < count do
                                        let r = random.Next(arr.Length)
                                        set <- Set.add r set

                                    Nodes (set |> Set.toList |> List.map (fun i -> arr.[i]))
                                else
                                    InsufficientRelays arr.Length
                             | Chain count ->
                                 let arr = all |> List.sortBy (fun (a,b,c,d) -> d) |> List.toArray
                                 if arr.Length >= count && count < 10 then
                                    let list = [0..count-1] |> List.map (fun i -> arr.[i])
                                    list |> List.iter (fun (a,b,_,_) -> increaseUseCount (a,b) |> ignore)
                                    printf "List: %A" list
                                    Nodes list
                                 else
                                    InsufficientRelays arr.Length
                                    


                    let arr = pickler.Pickle reply
                    let str = Convert.ToBase64String arr

                    writer.WriteLine str
                    writer.Flush()

            with
                | :? OperationCanceledException ->
                    Log.info "shutdown"

                    writer.Dispose()
                    reader.Dispose()
                    stream.Dispose()
                    client.Close()

                | e ->
                    Log.error "unexpected error: %A" e
        }

    let startListener() =
        async {
            Log.info "running"
            listener.Start()

            try
                while true do
                    let! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask

                    client |> startInstance |> start
            with 
                | :? OperationCanceledException ->
                    Log.info "shutdown"
                    listener.Stop()
                | e ->
                    Log.error "unexpected error: %A" e

        }
    

    member x.info fmt =
        Log.info fmt

    member x.WaitForChainNodes (count : int) =
        Log.info "waiting for %d chain nodes to come up" count
        while content.Count < count do
            Thread.Sleep(200)

        Log.info "finished waiting for chain nodes"

    member x.GetAllNodes() =
        getAllRelays()

    member x.PrintChainNodes() =
        for (KeyValue((address, port),(key, last, useCount))) in content do
            Log.info "%s:%d (last alive: %A) (used %d times)" address port last useCount
        ()

    member x.Run() =
        startPingListener() |> start
        startListener() |> Async.RunSynchronously

    member x.Start() =
        startPingListener() |> start
        startListener() |> start


    member x.Stop() =
        cancel.Cancel()

    member x.IncreaseUseCount (key : string * int) =
        increaseUseCount key

