namespace Matrjoshka

open Amazon.EC2.Model
open Amazon.EC2.Util
open Amazon.EC2
open Amazon
open System.IO
open System.Threading
open System.Threading.Tasks
open System

module EC2 =

    

    let private Log = Logging.logger "EC2"
    let private imageName = "G1-T3-Mono"
    let private securityGroup = "G1-T3-General"
    let private keyName = "IrelandKeys"

    let private chainStartupScript (directoryIp : string) (port : int) =
        //let str = sprintf "<script>cmd /C \"C:\\Matrjoshka\\Matrjoshka.exe chain %s %d\"</script>" directoryIp port

        let str = sprintf "#!/bin/bash\n/home/ubuntu/start chain %s %d" directoryIp port

        Log.info "startup script for chains: %A" str
        let bytes = System.Text.ASCIIEncoding.ASCII.GetBytes str
        Convert.ToBase64String(bytes)

    let private serviceStartupScript (port : int) =
        let str = sprintf "#!/bin/bash\n/home/ubuntu/start service %d" port

        Log.info "startup script for service: %A" str
        let bytes = System.Text.ASCIIEncoding.ASCII.GetBytes str
        Convert.ToBase64String(bytes)    

    type Credentials = { accessKeyId : string; secretAccessKey : string }

    let private parseCredentialsFile(content : string) =
        let lines = String.lines content |> List.toArray
        if lines.Length >= 2 then
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


    type EC2ChainPool(cred : Credentials, chainPort : int, servicePort : int) =
        inherit ChainPool()

        let myself = getMyOwnAddress()
        let client = new AmazonEC2Client(cred.accessKeyId, cred.secretAccessKey, RegionEndpoint.EUWest1)


        let startInstancesAsync (count : int) (startupScript : string) =
            async {

                Log.info "localhost: %A" myself


                let getImageId = DescribeImagesRequest()
                getImageId.Owners.Add "self"

                let images = client.DescribeImages(getImageId)

                let image = images.Images |> Seq.find (fun i -> i.Name = imageName)
                let imageId = image.ImageId

                let request = RunInstancesRequest(imageId, count, count)

                request.UserData <- startupScript
                request.InstanceType <- InstanceType.T2Micro
                request.SecurityGroups.Add securityGroup
                request.KeyName <- keyName
                                        

                // start instances
                Log.info "issuing request"
                let! response = client.RunInstancesAsync(request) |> Async.AwaitTask
                Log.info "got response with status: %A" response.HttpStatusCode


                // create tags (Name) for instances
                Log.info "creating tags"
                let tags = response.Reservation.Instances |> Seq.map(fun instance -> Tag("Name", "G1-T3-Chain")) |> Seq.toList
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
                    [ for instance in instances do
                
                        Log.info "instance %A ready: %A (%A)" instance.InstanceId instance.PrivateIpAddress instance.PublicIpAddress


                        // create a shutdown-function
                        let shutdown () =
                            async {
                                // issue the stop request
                                let r = StopInstancesRequest(System.Collections.Generic.List [instance.InstanceId])
                                let! res = client.StopInstancesAsync r |> Async.AwaitTask

                                // issue the terminate request
                                let term = TerminateInstancesRequest(System.Collections.Generic.List [instance.InstanceId])
                                let! res = client.StopInstancesAsync r |> Async.AwaitTask
                   
                                return ()
                            }

                        yield { privateAddress = instance.PrivateIpAddress; publicAddress = instance.PublicIpAddress; port = chainPort; shutdown = shutdown }
                                      
                    ]

                return instances
            }


        override x.StartChainAsync(count : int) =
            async {
                return! startInstancesAsync count (chainStartupScript myself chainPort)
            }

        override x.StartServiceAsync() =
            async {
                let! instance = startInstancesAsync 1 (serviceStartupScript servicePort)
                match instance with
                    | service::_ -> return service
                    | _ -> return failwith "could not start service"
            }

        override x.Dispose() = 
            client.Dispose()


    let createChainPool (credentialsFile : string) (chainPort : int) (directoryPort : int) (servicePort : int) : Error<ChainPool> =
        if not <| File.Exists credentialsFile then
            Log.error "could not find credentials-file"
            Error "credentials-file could not be found"
        else
            match parseCredentialsFile (File.ReadAllText credentialsFile) with
                | Success cred ->

                    try
                        let pool = new EC2ChainPool(cred, chainPort, servicePort)

                        Success (pool :> ChainPool)

                    with e ->
                        Log.error "could not connect to EC2: %A" e
                        Error (sprintf "%A" e)

                | Error e ->
                    Log.error "could not parse credentials-file"
                    Error e
