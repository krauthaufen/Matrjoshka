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

type ChainNodeHandle = { privateAddress : string; publicAddress : string; port : int; shutdown : unit -> Async<unit> }

[<AbstractClass>]
type ChainPool() =
    abstract member Dispose : unit -> unit
    abstract member StartChainAsync : int -> Async<list<ChainNodeHandle>>
    
    interface IDisposable with
        member x.Dispose() = x.Dispose()


module EC2 =

    let Log = Logging.logger "EC2"
    let private imageName = "G1-T3-Windows"
    let private startupScript (directoryIp : string) (port : int) =
        let str = sprintf "<script>cmd /C \"C:\\Matrjoshka\\Matrjoshka.exe chain %s %d\"</script>" directoryIp port

        Log.info "startup script for chains: %A" str
        let bytes = System.Text.ASCIIEncoding.Default.GetBytes str

        Convert.ToBase64String(bytes)


    type private Credentials = { accessKeyId : string; secretAccessKey : string }

    let private parseCredentialsFile(content : string) =
        let lines = String.lines content |> List.toArray
        if lines.Length = 2 then
            let lines = lines |> Array.map (fun l -> l.Trim())
            Success { accessKeyId = lines.[0]; secretAccessKey = lines.[1];}
        else
            Error "invalid credentials file-format"


    let private getMyOwnAddress () =
        try
            let request = System.Net.HttpWebRequest.Create("http://instance-data/latest/meta-data/public-ipv4")
            request.Timeout <- 1000
            let response = request.GetResponse()
            use reader = new System.IO.StreamReader(response.GetResponseStream())
            reader.ReadToEnd()
        with e ->
            Log.warn "error %A" e
            System.Environment.MachineName

    let createChainPool (credentialsFile : string) (chainPort : int) (directoryPort : int) : Error<ChainPool> =
        if not <| File.Exists credentialsFile then
            Log.error "could not find credentials-file"
            Error "credentials-file could not be found"
        else
            match parseCredentialsFile (File.ReadAllText credentialsFile) with
                | Success cred ->

                    let myself = getMyOwnAddress()

                    try
                        let client = new AmazonEC2Client(cred.accessKeyId, cred.secretAccessKey, RegionEndpoint.EUWest1)

                        let pool = 
                            { new ChainPool() with
                                member x.StartChainAsync(count : int) =
                                    async {

                                        Log.info "localhost: %A" myself


                                        let getImageId = DescribeImagesRequest()
                                        getImageId.Owners.Add "self"
//                                        getImageId.ExecutableUsers.Add "self"


                                        let images = client.DescribeImages(getImageId)

                                        let image = images.Images |> Seq.find (fun i -> i.Name = "G1-T3-Windows")
                                        let imageId = image.ImageId

                                        let request = RunInstancesRequest(imageId, count, count)

                                        request.UserData <- startupScript myself chainPort
                                        request.InstanceType <- InstanceType.T2Small
                                        request.SecurityGroups.Add "G1-T3-Windows"
                                        request.KeyName <- "G1-T3-Win"
                                        

                                        // start instances
                                        Log.info "issuing request"
                                        let! response = client.RunInstancesAsync(request) |> Async.AwaitTask
                                        Log.info "got response´with status: %A" response.HttpStatusCode


                                        // create tags (Name) for instances
                                        Log.info "creating tags"
                                        let tags = response.Reservation.Instances |> Seq.map(fun instance -> Tag("Name", sprintf "G1-T3-Chain")) |> Seq.toList
                                        let ids = System.Collections.Generic.List(response.Reservation.Instances |> Seq.map (fun i -> i.InstanceId))

                                        let createTags = CreateTagsRequest(ids, System.Collections.Generic.List(tags))
                                        let! res = client.CreateTagsAsync(createTags) |> Async.AwaitTask

                                        if res.HttpStatusCode <> Net.HttpStatusCode.OK then
                                            Log.warn "failed to create tags: %A" (res.ResponseMetadata.Metadata |> Seq.map (fun (KeyValue(k,v)) -> k,v) |> Seq.toList)

                                        


                                        // wait until all instances are ready
                                        let ready = ref false
                                        while not ready.Value do
                                            Log.info "checking instance status"
                                            let statusRequest = DescribeInstanceStatusRequest()
                                            statusRequest.InstanceIds <- ids
                                            statusRequest.IncludeAllInstances <- true

                                            let status = client.DescribeInstanceStatus(statusRequest)
                                            ready := status.InstanceStatuses |> Seq.forall(fun s -> s.InstanceState.Name = InstanceStateName.Running)
                                            
                                            if not ready.Value then
                                                Thread.Sleep 1000

                                        Log.info "all instances running"



                                        // get updated instance descriptions (including public ips)
                                        Log.info "getting updated instance descriptions"
    
                                        let inst = DescribeInstancesRequest()
                                        inst.InstanceIds <- ids
                         
                                        let! res = client.DescribeInstancesAsync(inst) |> Async.AwaitTask

                                        let instances = res.Reservations|> Seq.collect (fun res -> res.Instances) |> Seq.toList
                                        Log.info "got %d instances" instances.Length




                                        let instances = 
                                            [ for (instance) in instances do
                
                                                Log.info "instance %A ready: %A (%A)" instance.InstanceId instance.PrivateIpAddress instance.PublicIpAddress


                                                // create a shutdown-function
                                                let shutdown () =
                                                    async {
                                                        // issue the request
                                                        let r = StopInstancesRequest(System.Collections.Generic.List [instance.InstanceId])
                                                        let! res = client.StopInstancesAsync r |> Async.AwaitTask

                   
                                                        return ()
                                                    }

                                                yield { privateAddress = instance.PrivateIpAddress; publicAddress = instance.PublicIpAddress; port = chainPort; shutdown = shutdown }
                                      
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

            return { privateAddress = "127.0.0.1"; publicAddress = "127.0.0.1"; port = 12345; shutdown = fun () -> async { return node.Stop() } }
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

                            yield { privateAddress = "127.0.0.1"; publicAddress = "127.0.0.1"; port = port; shutdown = fun () -> async { return node.Stop() } }
                        ]

                    return instances
                }

            member x.Dispose() = ()        
        }




