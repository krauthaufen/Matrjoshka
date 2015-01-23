﻿open System
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

let chainNodeCount = 5
let chainNodeBasePort = 9985
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

        | [|"directory"; clientPort|] ->

            let port = Int32.Parse clientPort
//
//            let pool = 
//                match EC2.createChainPool "/home/ubuntu/cred.txt" chainNodeBasePort directoryPingPort with
//                    | Success pool -> pool
//                    | Error e -> 
//                        failwith e
            let pool = Sim.createChainPool 12345 directoryPingPort

            let chainNodeHandles = pool.StartChainAsync chainNodeCount |> Async.RunSynchronously

            let mapping = chainNodeHandles |> List.map (fun h -> h.privateAddress, h.publicAddress) |> Map.ofList

            let remapName (name : string) =
                match Map.tryFind name mapping with
                    | Some i -> i
                    | None -> name

            let d = Directory(port, directoryPingPort, remapName)
            d.Start()
            
            d.WaitForChainNodes(chainNodeCount)

            let mutable running = true
            while running do
                printf "dir# "
                let line = Console.ReadLine()

                match line with
                    | "!kill" -> ()
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

        | [|"service"; port|] ->

            let quotes =
                [|
                    "You know nothing, Jon Snow!"
                    "Brace yourself! Winter is coming!"
                    "When you play the game of thrones, you win or you die."
                    "Fear cuts deeper than swords."
                    "What do we say to the Lord of Death? - Not today."
                    "Noseless and Handless, the Lannister Boys."
                    "The North remembers."
                    "A Lannister always pays his debts."
                    "The man who passes the sentence should swing the sword."
                    "A dragon is not a slave."
                |]

            let r = System.Random()

            let pages =
                Map.ofList [
                    "/", fun _ ->
                        let id = r.Next(quotes.Length)
                        let q = quotes.[id]
                        q
                ]
            let s = HttpServer(System.Int32.Parse port, pages)

            s.Run()

            0
            

        | [|"client"; directory; directoryPort|] ->
            let c = Client(directory, Int32.Parse directoryPort)
            let mutable running = true

            ClientMonitor.run 8080 c


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
                        let data = c.Request(Request("http://localhost:1234/", 0, null)) |> Async.RunSynchronously

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
