namespace Babuschka3

open Amazon.EC2.Model
open Amazon.EC2.Util
open Amazon.EC2
open Amazon
open System.IO
open System.Threading
open System.Threading.Tasks
open System
open Babuschka3
open Babuschka

type Error<'a> = Success of 'a | Error of string

type ChainNodeHandle = { address : string; port : int; shutdown : unit -> Async<unit> }

[<AbstractClass>]
type ChainPool() =
    abstract member Dispose : unit -> unit
    abstract member StartChainAsync : unit -> Async<ChainNodeHandle>
    abstract member ChainNodes : list<ChainNodeHandle>
    
    interface IDisposable with
        member x.Dispose() = x.Dispose()

    member x.Shutdown() =
        async {
            
            let shutdownTasks = x.ChainNodes |> List.map (fun c -> c.shutdown())
            
            for task in shutdownTasks do
                let! () = task
                ()

            x.Dispose()
        }


module EC2 =
    let mutable private client : Option<AmazonEC2Client> = None

    type private Credentials = { accessKeyId : string; secretAccessKey : string }

    let private parseCredentialsFile(content : string) =
        let lines = String.lines content |> List.toArray
        if lines.Length = 2 then
            let lines = lines |> Array.map (fun l -> l.Trim())
            Success { accessKeyId = lines.[0]; secretAccessKey = lines.[1];}
        else
            Error "invalid credentials file-format"

    let private connect(credentialsFile : string) =
        if not <| File.Exists credentialsFile then
            Error (sprintf "could not find credential-file: %A" credentialsFile)
        else
            match parseCredentialsFile (File.ReadAllText credentialsFile) with
                | Success cred ->
                
                    // dispose the old client (if any)
                    match client with
                        | Some c -> c.Dispose()
                        | None -> ()

                    let c = new AmazonEC2Client(cred.accessKeyId, cred.secretAccessKey, RegionEndpoint.EUWest1)
                    client <- Some c

                    Success c

                | Error e ->
                    Error e

    let private disconnect() =
        match client with
            | Some c -> c.Dispose()
            | None -> ()

        client <- None


    let private getClient() =
        match client with
            | Some c -> c
            | None -> failwith "client was not connected"
    

    let startInstance() =
        async {
            let c = getClient()
            
            let runRequest = RunInstancesRequest("", 1, 1)

            let response = c.DryRun(runRequest)
            
            printfn "%A" response
            ()
        }

    let test() =
        match connect "cred.txt" with
            | Error e -> failwith e
            | _ -> ()

        startInstance() |> Async.RunSynchronously
        

        disconnect()
        
        printfn "done"


    let createChainPool (credentialsFile : string) (directoryPort : int) : Error<ChainPool> =
        if not <| File.Exists credentialsFile then
            Error "credentials-file could not be found"
        else
            match parseCredentialsFile (File.ReadAllText credentialsFile) with
                | Success cred ->

                    let client = new AmazonEC2Client(cred.accessKeyId, cred.secretAccessKey, RegionEndpoint.EUWest1)


                    let pool = 
                        { new ChainPool() with
                            member x.StartChainAsync() = failwith "not implemented"
                            member x.ChainNodes = []
                            member x.Dispose() = client.Dispose()
                        }

                    Success pool

                | Error e ->
                    Error e



module Sim =

    let test =
        async {
            let node = Babuschka3.Relay("127.0.0.1", 12345, sprintf "c%d" 54321, 54321)

            node.Start() |> ignore

            return { address = "127.0.0.1"; port = 12345; shutdown = fun () -> async { return node.Stop() } }
        }

    let createChainPool (basePort : int) (directoryPort : int) =
        let currentPort = ref basePort

        { new ChainPool() with

            member x.StartChainAsync () : Async<ChainNodeHandle> =
                async {
                    let port = Interlocked.Increment(&currentPort.contents)

                    let node = Relay("127.0.0.1", directoryPort, sprintf "c%d" port, port)

                    node.Start() |> ignore

                    return { address = "127.0.0.1"; port = port; shutdown = fun () -> async { return node.Stop() } }
                }

            member x.Dispose() = ()        
            member x.ChainNodes = []
        }




