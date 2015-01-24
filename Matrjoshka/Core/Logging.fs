namespace Matrjoshka

open System
open System.Text.RegularExpressions

module String =
    let private lineBreak = Regex System.Environment.NewLine

    let lines (str : string) =
        lineBreak.Split(str) |> Array.toList

    let empty (length : int) =
        System.String(' ', length)

type ILogger =
    abstract member WriteLine : string -> unit
    abstract member WriteColoredLine : int -> string -> unit

type ConsoleLogger() =

    static let colors =
        [
            0x000000, ConsoleColor.Black
            0xFFFFFF, ConsoleColor.White
            0x0000FF, ConsoleColor.Blue
            0x00FF00, ConsoleColor.Green
            0xFF0000, ConsoleColor.Red
            0xFFFF00, ConsoleColor.Yellow
            0xFF00FF, ConsoleColor.Magenta
            0x00FFFF, ConsoleColor.Cyan

            0x7F7F7F, ConsoleColor.Gray
            0x00007F, ConsoleColor.DarkBlue
            0x007F00, ConsoleColor.DarkGreen
            0x7F0000, ConsoleColor.DarkRed
            0x7F7F00, ConsoleColor.DarkYellow
            0x7F007F, ConsoleColor.DarkMagenta
            0x007F7F, ConsoleColor.DarkCyan


        ] |> Map.ofList

    member x.WriteLine (line : string) =
        let lines = String.lines line
        for l in lines do
            Console.WriteLine("{0}", line)

    member x.WriteColoredLine (color : int) (line : string) =
        let store = Console.ForegroundColor
        Console.ForegroundColor <- Map.find color colors

        let lines = String.lines line
        for l in lines do
            Console.WriteLine("{0}", line)

        Console.ForegroundColor <- store

    interface ILogger with
        member x.WriteLine line = x.WriteLine line
        member x.WriteColoredLine color line = x.WriteColoredLine color line
            
type NamedLogger(name : string, inner : ILogger) =
    let emptyName = String.empty (name.Length)

    member x.WriteLine(line : string) =
        let lines = String.lines line
        match lines with
            | l0::lines ->
                inner.WriteLine(sprintf "%s# %s" name l0)

                for l in lines do
                    inner.WriteLine(sprintf "%s  %s" emptyName l)
            | [] ->
                inner.WriteLine(sprintf "%s# %s" name line)

    member x.WriteColoredLine (color : int) (line : string) =
        let lines = String.lines line
        match lines with
            | l0::lines ->
                inner.WriteColoredLine color (sprintf "%s# %s" name l0)

                for l in lines do
                    inner.WriteColoredLine color (sprintf "%s  %s" emptyName l)
            | [] ->
                inner.WriteColoredLine color (sprintf "%s# %s" name line)

    interface ILogger with
        member x.WriteLine line = x.WriteLine line
        member x.WriteColoredLine color line = x.WriteColoredLine color line

type MonitorableHtmlLogger(port : int) =
    let builder = System.Text.StringBuilder()


    let template =
        """
        <html>
            <head>
                <title>Matrjohska Log</title>

                <style type="text/css">
                    body {
                        background: #000000;
                        color: #FFFFFF;
                        font-family: Consolas;
                    }

                    .content {
                        padding-left: 20px;
                    }
                </style>

                <script src="http://code.jquery.com/jquery-1.9.1.min.js"></script>
                <script type="text/javascript">
                    var content = null;
                    var last = null;

                    function reload() {
                        try {
                            var request = new XMLHttpRequest();

                            request.onreadystatechange = function() {
                                if(request.readyState == 4 && request.status == 200 && last != request.responseText) {
                                    content.innerHTML = request.responseText;
                                    last = request.responseText;
                                    window.scrollTo(0,document.body.scrollHeight);
                                }
                            };

                            request.open("GET", document.URL, true);
                            request.send();

                            setTimeout('reload()', 500);
                            
                        }
                        catch(e) {
                            console.log("error");
                        }
                    }
                    $( document ).ready(function() {
                        content = document.getElementById("content");
                        reload();
                    });

                </script>
            </head>
            <body>
                <div id="content" class="content">
                    {Content}
                </div>
            </body>

        </html>
        """

    do
        let listener = new System.Net.HttpListener()
        #if WINDOWS
        listener.Prefixes.Add ("http://localhost:" + string port + "/")
        #else
        listener.Prefixes.Add ("http://*:" + string port + "/")
        #endif
        listener.Start()

        let run =
            async {
                while true do
                    let! c = listener.GetContextAsync() |> Async.AwaitTask


                    match c.Request.Url.LocalPath with
                        | "/" ->
                            let str = builder.ToString()

                            let str = 
                                if str.Length > 100000 then
                                    str.Substring(str.Length - 100000, 100000)
                                else
                                    str

                            let str = template.Replace("{Content}", str)


                            let bytes = System.Text.ASCIIEncoding.UTF8.GetBytes(str)
                            c.Response.ContentType <- "text/html"
                            c.Response.StatusCode <- 200

                            c.Response.ContentLength64 <- bytes.LongLength
                            c.Response.OutputStream.Write(bytes, 0, bytes.Length)
                            
                        | _ ->
                            c.Response.ContentLength64 <-0L
                            c.Response.OutputStream.Write([||], 0, 0)
                            c.Response.StatusCode <- 404

            }

        run |> Async.StartAsTask |> ignore

    let hexColor (c : int) =
        sprintf "#%02X%02X%02X" ((c &&& 0xFF0000) >>> 16) ((c &&& 0x00FF00) >>> 8) (c &&& 0x0000FF)

    member x.WriteColoredLine (color : int) (line : string) =
        builder.AppendFormat("<div style=\" color: {0}; \">{1}</div>\r\n", hexColor color, line) |> ignore

    member x.WriteLine (line : string) =
        builder.AppendFormat("<div>{0}</div>\r\n", line) |> ignore

    interface ILogger with
        member x.WriteLine line = x.WriteLine line
        member x.WriteColoredLine color line = x.WriteColoredLine color line

type MultiLogger (loggers : list<ILogger>) =
    
    interface ILogger with
        member x.WriteLine str =
            for l in loggers do l.WriteLine str

        member x.WriteColoredLine col str =
            for l in loggers do l.WriteColoredLine col str


[<AutoOpen>]
module Logging =
    let private cons = ConsoleLogger() :> ILogger
    let private html = MonitorableHtmlLogger(9998) :> ILogger

    let private real = MultiLogger [cons; html] :> ILogger

    let logger (name : string) =
        NamedLogger(name, real) :> ILogger

    type ILogger with
        member x.debug fmt = Printf.kprintf (fun str -> x.WriteLine str) fmt
        member x.info fmt = Printf.kprintf (fun str -> x.WriteColoredLine 0xFFFFFF str) fmt
        member x.warn fmt = Printf.kprintf (fun str -> x.WriteColoredLine 0x7F7F00 str) fmt
        member x.error fmt = Printf.kprintf (fun str -> x.WriteColoredLine 0xFF0000 str) fmt
