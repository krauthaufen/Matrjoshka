namespace Matrjoshka

open System
open System.Net
open System.Threading
open System.Threading.Tasks

module ClientUI =
    let run (port : int) (c : Client) =
        let serviceAddress = ref None
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

                        let serviceURL =
                            match !serviceAddress with
                                | Some url -> url
                                | None ->
                                    let (sa, sp) = c.GetServiceAddress().Value
                                    let serviceURL = sprintf "http://%s:%d/" sa sp
                                    serviceAddress := Some serviceURL
                                    serviceURL

                        sw.Start()



                        let data = c.Request(Request(serviceURL, 0, null)) |> Async.RunSynchronously
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

        let path = System.IO.Path.Combine("..", "..", "Matrjoshka", "WebServer", "static")
        let s = HttpServer(port, pages, path)

        s.Start() //Run()
        //System.Environment.Exit(0)
