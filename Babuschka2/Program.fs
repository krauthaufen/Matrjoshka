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
open Babuschka
open Babuschka3

let chainNodeCount = 3
let chainNodeBasePort = 1234
let directoryPingPort = 9980

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
        | [|"chain"; directory; listenPort|] ->
            let c = Babuschka3.Relay(directory, directoryPingPort, "chain", Int32.Parse listenPort)
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

        | [|"directory"; clientPort|] ->

            let port = Int32.Parse clientPort

            let pool = Sim.createChainPool chainNodeBasePort directoryPingPort

            let chainNodeHandles =
                [0..chainNodeCount-1] |> List.map (fun i -> pool.StartChainAsync() |> Async.RunSynchronously)

            let d = Babuschka3.Directory(port, directoryPingPort)
            d.Start()
            
            d.WaitForChainNodes(chainNodeCount)

            let mutable running = true
            while running do
                printf "dir# "
                let line = Console.ReadLine()

                match line with
                    | "!shutdown" ->
                        chainNodeHandles |> List.iter(fun c -> c.shutdown() |> Async.RunSynchronously) 

                        pool.Dispose()
                        d.Stop()

                        running <- false
                        
                    | "!chain" ->
                        d.PrintChainNodes()
                    | _ ->
                        ()

            printfn "bye!"

            0

        | [|"client"; directory; directoryPort|] ->
            let c = Babuschka3.Originator(directory, Int32.Parse directoryPort)
            let mutable running = true

            while running do
                printf "client# "
                let l = Console.ReadLine()
                
                match l with
                    | "!shutdown" ->
                        c.Disconnect()
                        running <- false
                    | "!connect" ->
                        printfn "%A" <| c.Connect(3)

                    | "!ping" ->
                        c.Send(Command("somecommand", "content"))
                         
                    | _ -> () 

            0

        | _ ->
            usage()
            1
