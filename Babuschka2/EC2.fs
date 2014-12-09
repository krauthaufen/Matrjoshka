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
    
    interface IDisposable with
        member x.Dispose() = x.Dispose()


module EC2 =
    type private Credentials = { accessKeyId : string; secretAccessKey : string }

    let private parseCredentialsFile(content : string) =
        let lines = String.lines content |> List.toArray
        if lines.Length = 2 then
            let lines = lines |> Array.map (fun l -> l.Trim())
            Success { accessKeyId = lines.[0]; secretAccessKey = lines.[1];}
        else
            Error "invalid credentials file-format"

    let Log = Logging.logger "EC2"

    let private instanceId = ""
    let private startupScript (directoryIp : string) (port : int) =
        sprintf @"C:\Matrjoshka\run.exe chain %s %d" directoryIp port

    let createChainPool (credentialsFile : string) (chainPort : int) (directoryPort : int) : Error<ChainPool> =
        if not <| File.Exists credentialsFile then
            Log.error "could not find credentials-file"
            Error "credentials-file could not be found"
        else
            match parseCredentialsFile (File.ReadAllText credentialsFile) with
                | Success cred ->

                    try
                        let client = new AmazonEC2Client(cred.accessKeyId, cred.secretAccessKey, RegionEndpoint.EUWest1)

                        let pool = 
                            { new ChainPool() with
                                member x.StartChainAsync() =
                                    async {

                                        let request = RunInstancesRequest(instanceId, 1, 1)
                                        request.UserData <- startupScript "my ip" chainPort

                                        // spin until the request is successful
                                        let! response = client.RunInstancesAsync(null) |> Async.AwaitTask
                                        let response = ref response

                                        while response.Value.HttpStatusCode <> Net.HttpStatusCode.OK do
                                            Thread.Sleep 200
                                            let! r = client.RunInstancesAsync(null) |> Async.AwaitTask
                                            response := r
                                        let response = !response


                                        // get the created instance (must only be one)
                                        let instance = response.Reservation.Instances |> Seq.head
                                                
                                        // spin until the instance is "running"
                                        while instance.State.Name <> InstanceStateName.Running do
                                            Thread.Sleep(200)

                                        // create a shutdown-function
                                        let shutdown () =
                                            async {
                                                // issue the request
                                                let r = StopInstancesRequest(System.Collections.Generic.List [])
                                                let! res = client.StopInstancesAsync r |> Async.AwaitTask

                                                // spin until the instance-state is "terminated"
                                                let instance = res.StoppingInstances |> Seq.head
                                                while instance.CurrentState.Name <> InstanceStateName.Terminated do
                                                    Thread.Sleep 200

                                                return ()
                                            }

                                        return { address = instance.SubnetId; port = chainPort; shutdown = shutdown }
                      
                                    }

                                member x.Dispose() = 
                                    client.Dispose()
                            }

                        Success pool

                    with e ->
                        Log.error "could not connect to EC2: %A" e
                        Error (sprintf "%A" e)

                | Error e ->
                    Log.error "could not parse credentials-file"
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
        }




