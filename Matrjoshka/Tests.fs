#if INTERACTIVE
#r @"E:\Development\Babuschka2\packages\FsPickler.1.0.3\lib\net45\FsPickler.dll"
#else
namespace Matrjoshka
#endif

open System
open System.Net
open System.IO
open System.Net.Sockets
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open Nessos.FsPickler
open Nessos.FsPickler.Combinators
open Matrjoshka.Cryptography

module Test =

    let get() =
        use client = new TcpClient("www.google.de", 80, NoDelay = true)
        use stream = client.GetStream()
        let data = System.Text.ASCIIEncoding.ASCII.GetBytes("""GET http://www.google.de/index.html HTTP/1.0
Cache-Control: max-age=0
Connection: keep-alive
Accept: text/html,application/xhtml+xml,application/xml
        """)
        stream.Write(data, 0, data.Length)

        let buffer = Array.zeroCreate (1024)
        let read = stream.Read(buffer, 0, buffer.Length)
        let result = Array.sub buffer 0 read

        let res = System.Text.ASCIIEncoding.Default.GetString result
        printfn "%A" res


    let run() =
//        get()
//        Environment.Exit(0)

        // create and start a directory node
        let dir = Directory(12345, 54321, id)
        dir.Start()

        // build some relays
        let relays = 
            List.init 3 (fun i ->
                let ri = Relay("localhost", 54321, sprintf "r%d" i, 10000 + i)
                ri.Start()
                ri
            )

        // wait a second (so that all relays have registered with the directory)
        Thread.Sleep(1000)


        // let's create an originator
        let o = Client("localhost", 12345)

        Console.WriteLine "press enter to continue"
        Console.ReadLine() |> ignore

        Console.WriteLine "cont"

        // we'd like to have a chain of length 6 here
        o.Connect 3 |> ignore

        // send a command (currently only printed in the exit-node)
        o.Send(Command("somecommand", "content"))

        //relays |> List.iter (fun r -> r.Stop())

        Console.WriteLine "sadasdasdasd"
        Console.ReadLine() |> ignore

        let data = System.Text.ASCIIEncoding.Default.GetBytes("GET / HTTP/1.0")
        let data : Response = o.Request(Request("www.orf.at", 80, data)) |> Async.RunSynchronously

        // wait for user input
        Console.WriteLine "press enter to destroy the Originator"
        Console.ReadLine() |> ignore

        o.Disconnect()

        Console.WriteLine "press enter to create a new Originator"
        Console.ReadLine() |> ignore

        // we'd like to have a chain of length 3 this time
        let o = Client("localhost", 12345)
        
        o.Connect 3 |> ignore

        // send a command (currently only printed in the exit-node)
        o.Send(Command("somecommand", "content"))


        Console.WriteLine "press enter to destroy the Originator"
        Console.ReadLine() |> ignore


        o.Disconnect()

        Console.WriteLine "press enter to stop the directory and all relays"
        Console.ReadLine() |> ignore

        relays |> List.iter (fun r -> r.Stop())
        dir.Stop()

        Console.WriteLine "press enter to exit the program"
        Console.ReadLine() |> ignore




    
    