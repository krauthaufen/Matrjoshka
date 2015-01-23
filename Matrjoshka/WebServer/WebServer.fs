namespace Matrjoshka

open System
open System.Net
open System.Threading
open System.Threading.Tasks

type HttpServer(port : int, pages : Map<string, HttpListenerRequest -> string>, ?directory : string) =
    let listener = new System.Net.HttpListener()
    let cancel = new CancellationTokenSource()
    let Log = Logging.logger (sprintf "http:%d" port)
    let defaultPage = "<html><head><title>Not Found</title></head><body><h1>not found</h1></body></html>"

    do
        listener.Prefixes.Add ("http://localhost:" + string port + "/")
        listener.Start()

    let run =
        async {
            try
                while true do
                    let! c = listener.GetContextAsync() |> Async.AwaitTask

                    match Map.tryFind c.Request.Url.LocalPath pages with
                        | Some pageFun ->
                            let str = pageFun c.Request

                            let bytes = System.Text.ASCIIEncoding.UTF8.GetBytes(str)
                            c.Response.ContentLength64 <- bytes.LongLength
                            c.Response.OutputStream.Write(bytes, 0, bytes.Length)
                            c.Response.ContentType <- "text/html"
                            c.Response.StatusCode <- 200
                        | _ ->
                            match directory with
                                | Some directory ->
                                    let path = c.Request.Url.LocalPath.Replace('/', System.IO.Path.DirectorySeparatorChar)

                                    let path =
                                        if path.[0] = System.IO.Path.DirectorySeparatorChar then path.Substring 1
                                        else path

                                    let path = System.IO.Path.Combine(directory, path)
                                    //printfn "looking up: %A" path

                                    let probes =
                                        [path; System.IO.Path.ChangeExtension(path, ".html"); System.IO.Path.ChangeExtension(path, ".htm")]

                                    let path = probes |> List.tryFind System.IO.File.Exists
                                    match path with
                                        | Some path ->
                                            let mime = System.Web.MimeMapping.GetMimeMapping(path)
                                            let bytes = System.IO.File.ReadAllBytes(path)
                                            c.Response.ContentLength64 <- bytes.LongLength
                                            c.Response.OutputStream.Write(bytes, 0, bytes.Length)
                                            c.Response.ContentType <- mime
                                            c.Response.StatusCode <- 200
                                        | _ ->
                                            c.Response.StatusCode <- 404
                                | _ ->
                                    c.Response.StatusCode <- 404
                with 
                    | :? OperationCanceledException -> () //shutdown
                    | e -> Log.error "%A" e
        }

    member x.Run() =
        Async.RunSynchronously run

    member x.Start(?ct : CancellationToken) =
        let ct =
            match ct with
                | None -> cancel.Token
                | Some ct -> ct

        Async.StartAsTask(run, cancellationToken = ct) |> ignore

    member x.Stop() =
        cancel.Cancel()

[<AutoOpen>]
module HttpRequestExtensions =
    type System.Collections.Specialized.NameValueCollection with
        member x.TryGet(name : string) =
            let res = x.Get(name)
            if res = null then None
            else
                let elements = res.Split ',' |> Array.toList
                Some elements


module ClientMonitor =
    let run (port : int) (c : Client) (serviceAddress : string) =

        let pages =
            Map.ofList [
                "/chain", fun (r : HttpListenerRequest) ->
                    let id = r.QueryString.TryGet "id"
                    match id with
                        | Some [id] ->
                            
                            match c.TryGetChainIP (System.Int32.Parse id) with
                                | Some (ip, _) -> sprintf "%s" ip
                                | None ->
                                    "xxx.xxx.xxx.xxx"

                        | _ ->
                            "xxx.xxx.xxx.xxx"

                "/connect", fun (r : HttpListenerRequest) ->
                    let sw = System.Diagnostics.Stopwatch()
                    sw.Start()
                    match c.Connect(3) with
                        | Success() -> 
                            sw.Stop()
                            sprintf "{ \"status\" : 1, \"took\": \"%fm\" }" sw.Elapsed.TotalMilliseconds
                        | Error e -> 
                            sw.Stop()
                            sprintf "{ \"status\" : 0, \"error\": \"%s\" }" e

                "/qod", fun (r : HttpListenerRequest) ->
                    try
                        let sw = System.Diagnostics.Stopwatch()
                        sw.Start()
                        let data = c.Request(Request(serviceAddress, 0, null)) |> Async.RunSynchronously
                        sw.Stop()

                        match data with
                            | Data content ->
                                let quote = System.Text.ASCIIEncoding.UTF8.GetString content
                                sprintf "{ \"status\" : 1, \"quote\": \"%s\", \"time\": %f }"  quote sw.Elapsed.TotalMilliseconds
                            | _ ->
                                sprintf "{ \"status\" : 0, \"error\": \"Invalid Response\" }"
                    with e ->
                        sprintf "{ \"status\" : 0, \"error\": \"%s\" }" e.Message
            ]

        let s = HttpServer(port, pages, @"E:\Development\Matrjoshka\Matrjoshka\WebServer\static")

        s.Start() //Run()
        //System.Environment.Exit(0)
