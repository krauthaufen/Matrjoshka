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
    let defaultPageBytes = System.Text.ASCIIEncoding.UTF8.GetBytes defaultPage

    do
        #if WINDOWS
        listener.Prefixes.Add ("http://localhost:" + string port + "/")
        #else
        listener.Prefixes.Add ("http://*:" + string port + "/")
        #endif
        listener.Start()

    let run =
        async {
            try
                while true do
                    let! c = listener.GetContextAsync() |> Async.AwaitTask
                    let response = c.Response
                    match Map.tryFind c.Request.Url.LocalPath pages with
                        | Some pageFun ->
                            let str = pageFun c.Request

                            let bytes = System.Text.ASCIIEncoding.UTF8.GetBytes(str)
                            response.ContentType <- "text/html"
                            response.StatusCode <- 200

                            response.ContentLength64 <- bytes.LongLength
                            response.OutputStream.Write(bytes, 0, bytes.Length)
                            
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
                                            response.ContentType <- mime
                                            response.StatusCode <- 200

                                            response.ContentLength64 <- bytes.LongLength
                                            response.OutputStream.Write(bytes, 0, bytes.Length)
                                            
                                            
                                        | _ ->
                                            response.StatusCode <- 404
                                            response.ContentLength64 <- defaultPageBytes.LongLength
                                            response.OutputStream.Write(defaultPageBytes, 0, defaultPageBytes.Length)
                                | _ ->
                                    response.StatusCode <- 404
                                    response.ContentLength64 <- defaultPageBytes.LongLength
                                    response.OutputStream.Write(defaultPageBytes, 0, defaultPageBytes.Length)


                        
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

