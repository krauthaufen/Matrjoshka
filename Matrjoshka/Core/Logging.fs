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

[<AutoOpen>]
module Logging =
    let private real = ConsoleLogger() :> ILogger

    let logger (name : string) =
        NamedLogger(name, real) :> ILogger

    type ILogger with
        member x.debug fmt = Printf.kprintf (fun str -> x.WriteLine str) fmt
        member x.info fmt = Printf.kprintf (fun str -> x.WriteColoredLine 0xFFFFFF str) fmt
        member x.warn fmt = Printf.kprintf (fun str -> x.WriteColoredLine 0x7F7F00 str) fmt
        member x.error fmt = Printf.kprintf (fun str -> x.WriteColoredLine 0xFF0000 str) fmt
