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
    abstract member StartChainAsync : int -> Async<list<ChainNodeHandle>>
    
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

    let private instanceId = "ami-862a96f1"
    let private startupScript (directoryIp : string) (port : int) =
        let str = sprintf @"C:\Matrjoshka\run.exe chain %s %d" directoryIp port
        let bytes = System.Text.ASCIIEncoding.Default.GetBytes str

        Convert.ToBase64String(bytes)


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
                                member x.StartChainAsync(count : int) =
                                    async {

                                        Log.info "localhost: %A" (System.Net.Dns.GetHostEntry("localhost").AddressList)

                                        let request = RunInstancesRequest(instanceId, count, count)
                                        request.UserData <- startupScript "54.154.32.116" chainPort
                                        request.InstanceType <- InstanceType.T2Micro
                                        request.SecurityGroups.Add "G1-T3-Windows"
                                        request.KeyName <- "G1-T3-Win"
                              
                                        Log.info "performing dry run"
                                        let dry = client.DryRun(request)
                                        if dry.IsSetError() then
                                            Log.error "dry run failed: %A" dry.Error
                                            failwith "%A" dry.Error


                                        Log.info "issuing request"
                                        // spin until the request is successful
                                        let! response = client.RunInstancesAsync(request) |> Async.AwaitTask
                                        //let response = ref response
                                        Log.info "got response: %A" response

//
//                                        while response.Value.HttpStatusCode <> Net.HttpStatusCode.OK do
//                                            Thread.Sleep 200
//                                            Log.info ""
//                                            let! r = client.RunInstancesAsync(request) |> Async.AwaitTask
//                                            response := r
//                                        let response = !response


                                        let instances = response.Reservation.Instances |> Seq.toList |> List.mapi (fun i instance -> i,instance)
                                        Log.info "got %d instance handles" instances.Length

                                        let instances = 
                                            [ for (id, instance) in instances do

                                                instance.Tags.Add(Tag("name", sprintf "G1-T3-Chain%d" id))
                                                //Log.info "waiting for instance: %A" instance.PublicDnsName
                                                // spin until the instance is "running"
                                                while instance.State.Name <> InstanceStateName.Running do
                                                    Log.info "waiting for instance: %A" instance.PublicDnsName
                                                    Thread.Sleep(4000)
                                                    
                                                

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

                                                Log.info "instance %A ready" instance.PublicIpAddress
                                                yield { address = instance.SubnetId; port = chainPort; shutdown = shutdown }
                                            ]

                                        return instances
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

            member x.StartChainAsync (count : int) =
                async {
                    let instances = 
                        [ for _ in 0..count-1 do
                            let port = Interlocked.Increment(&currentPort.contents)

                            let node = Relay("127.0.0.1", directoryPort, sprintf "c%d" port, port)

                            node.Start() |> ignore

                            yield { address = "127.0.0.1"; port = port; shutdown = fun () -> async { return node.Stop() } }
                        ]

                    return instances
                }

            member x.Dispose() = ()        
        }




