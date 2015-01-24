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
open Matrjoshka
open Matrjoshka.Cryptography

let chainNodeCount = 6
let chainNodeBasePort = 9984
let directoryPingPort = 9980
let servicePort = 9981

let usage() =
    printfn "matrjoshka supports the following commands:"
    printfn ""
    printfn "    client <directory-ip> <directory-port>"
    printfn "        the client connects to the directory node"
    printfn "        using the given ip (may also be a hostname) and port"
    printfn ""
    printfn "    directory <tcp-port>"
    printfn "        the directory waits for 'alive'-messages at %d" directoryPingPort
    printfn "        and accepts connections (from clients) at tcp-port"
    printfn ""
    printfn "    chain <directory-ip> <tcp-port>"
    printfn "        the chain node sends alive-messages to the directory"
    printfn "        and accepts client-connections at tcp-port" 

[<EntryPoint>]
let main args =

    match args with

        //start a chain node
        | [|"chain"; directory; listenPort|] ->
            let c = Relay(directory, directoryPingPort, "chain", Int32.Parse listenPort)
            c.Start()

            let mutable running = true
            while running do
                let line = Console.ReadLine()
                match line with
                    | "!shutdown" -> 
                        c.Stop()
                        running <- false
                    | _ ->
                        ()

            0

        // start the directory node spawning its own chain nodes
        | [|"directory"; clientPort|] ->

            let port = Int32.Parse clientPort


//            let pool = 
//                match EC2.createChainPool "/home/ubuntu/cred.txt" chainNodeBasePort directoryPingPort with
//                    | Success pool -> pool
//                    | Error e -> 
//                        failwith e
            let pool = Sim.createChainPool 12345 directoryPingPort servicePort


            let serviceHandle = pool.StartServiceAsync() |> Async.RunSynchronously

            let remapName (name : string) =
                match Map.tryFind name !mapping with
                    | Some i -> i
                    | None -> name

            let d = Directory(port, directoryPingPort, remapName, serviceHandle.publicAddress, serviceHandle.port)
            d.Start()
            
            d.WaitForChainNodes(chainNodeCount)

            let restartDeadInstances =
                async {
                    while true do
                        do! Async.Sleep 5000
                        let living = d.GetAllNodes() |> List.length

                        if living < chainNodeCount then
                            d.info "%d instances living" living

                        let missing = chainNodeCount - living
                        if missing > 0 then
                            d.info "restarting %d instances" missing
                            let! handles = pool.StartChainAsync missing
                            for h in handles do
                                mapping := Map.add h.privateAddress h.publicAddress !mapping
                        else
                            ()

                }

            restartDeadInstances |> Async.StartAsTask |> ignore

            let rand = System.Random()
            let mutable running = true
            while running do
                printf "dir# "
                let line = Console.ReadLine()

                match line with
                    | "!kill" ->
                        let id = rand.Next(chainNodeHandles.Length)

                        let c = chainNodeHandles.[id]
                        c.shutdown() |> Async.RunSynchronously


                    | "!shutdown" ->
                        chainNodeHandles |> Array.iter(fun c -> c.shutdown() |> Async.RunSynchronously) 
                        serviceHandle.shutdown() |> Async.RunSynchronously

                        pool.Dispose()
                        d.Stop()

                        running <- false
                        
                    | "!chain" ->
                        d.PrintChainNodes()
                    | _ ->
                        ()

            printfn "bye!"

            0


        // start the service
        | [|"service"; port|] ->

            let s = Service(System.Int32.Parse port)
            s.Run()
            0
            
        // start the client
        | [|"client"; directory; directoryPort|] ->
            let c = Client(directory, Int32.Parse directoryPort)
            let mutable running = true

            ClientUI.run 8080 c "http://localhost:1234/"

            let (sa, sp) = c.GetServiceAddress().Value

            while running do
                printf "client# "
                let l = Console.ReadLine()
                
                match l with
                    | "!shutdown" ->
                        c.Disconnect()
                        running <- false
                    | "!connect" ->
                        printfn "%A" <| c.Connect(3)

                    | "!google" ->
                        let data = c.Request(Request("http://www.orf.at", 0, null)) |> Async.RunSynchronously

                        match data with
                            | Data content ->
                                printfn "got:\r\n\r\n%s" (System.Text.ASCIIEncoding.UTF8.GetString content)
                            | Exception e->
                                printfn "ERROR: %A" e
                            | _ ->
                                ()

                    | "!qod" ->
                        let data = c.Request(Request(sprintf "http://%s:%d/" sa sp, 0, null)) |> Async.RunSynchronously

                        match data with
                            | Data content ->
                                printfn "got:\r\n\r\n%s" (System.Text.ASCIIEncoding.UTF8.GetString content)
                            | Exception err ->
                                printfn "ERROR: %A" err
                            | _ ->
                                ()           
                         
                    | _ -> () 

            0

        | _ ->
            usage()
            1
