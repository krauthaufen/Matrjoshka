namespace Matrjoshka

open System
open System.Net
open System.Threading
open System.Threading.Tasks

type HttpServer(port : int, pages : Map<string, HttpListenerRequest -> string>, ?defaultPage : string) =
    let listener = new System.Net.HttpListener()
    let cancel = new CancellationTokenSource()
    let Log = Logging.logger (sprintf "http:%d" port)
    let defaultPage = defaultArg defaultPage "<html><head><title>Not Found</title></head><body><h1>not found</h1></body></html>"

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


module WebServerTest =
    let mainPage (r : HttpListenerRequest) =
        @"<html>
            <head>
                <title>Index</title>
            </head>
            <body>
                <h1>Index</h1>
                Some Text
            </body>
          </html>"

    let test (r : HttpListenerRequest) =
        let id = r.QueryString.TryGet "id"
        match id with
            | Some id ->
                @"<html>
                    <head>
                        <title>Index</title>
                    </head>
                    <body>
                        <h1>Your Query was:</h1>" + (sprintf "%A" id) +
                @"
                    </body>
                  </html>"
            | None ->
                @"<html>
                    <head>
                        <title>Index</title>
                    </head>
                    <body>
                        <h1>Please enter a query</h1>
                    </body>
                  </html>"

    let run() =

        let pages =
            Map.ofList [
                "/", mainPage
                "/test", test
            ]
        let s = HttpServer(8080, pages)

        s.Run()
        System.Environment.Exit(0)
