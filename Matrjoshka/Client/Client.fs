namespace Matrjoshka

open System
open System.IO
open System.Threading
open System.Net
open System.Net.Sockets
open Nessos.FsPickler
open Matrjoshka.Cryptography

type Client(directory : string, port : int) =
    static let pickler = FsPickler.CreateBinary(true)
    static let Log = Logging.logger "client"

    let mutable isConnected = false
    let socket = PlainSocket()

    let tryConnect() =
        if isConnected then true
        else
            try 
                socket.Connect(directory, port)
                isConnected <- true
                true
            with _ ->
                Log.warn "could not connect to directory"
                false

    do tryConnect() |> ignore

    let mutable currentChain = [||]

    let getChain(req: int->DirectoryRequest, l:int) =
        if tryConnect() then
            let r = req l
            try
                socket.Send(r)
                let reply = socket.Receive() |> Async.RunSynchronously

                match reply with
                    | Nodes list ->
                        list
                    | _ ->
                        []
            with _ ->
                []
        else
            []


    let getService() =
        if tryConnect() then
            try
                socket.Send DirectoryRequest.Service
                let response = socket.Receive() |> Async.RunSynchronously
                match response with
                    | DirectoryResponse.Address (address, port) ->
                        Some (address, port)
                    | _ ->
                        None

            with _ ->
                None
        else
            None

    let getNewChain(l: int) =
        getChain (DirectoryRequest.Chain, l)

    let getRandomChain(l : int) =
        getChain (DirectoryRequest.Random, l)


    let rec builChain (chain : list<string * int * RsaPublicKey * int>) =
        match chain with
            | [(remote,port,key, useCount)] ->
                let plain = PlainSocket()
                plain.Connect(remote, port)
                //Thread.Sleep(1000)

                let sec = SecureSocket(plain)
                match sec.Connect key with
                    | Success() -> Choice1Of2 sec
                    | Error e -> Choice2Of2 e

            | (remote, port, key, useCount)::rest ->
                match builChain rest with
                    | Choice2Of2 e -> Choice2Of2 e
                    | Choice1Of2 inner ->
                        let fw = FowardSocket(remote, port, inner)

                        let sec = SecureSocket(fw)
                        match sec.Connect key with
                            | Success() -> Choice1Of2 sec
                            | Error e -> Choice2Of2 e

            | [] ->
                failwith "cannot establish empty chain"

    let mutable client : Option<SecureSocket> = None




    member x.TryGetChainIP(index : int) =
        if currentChain.Length > index && index >= 0 then
            let (ip, port, _,_) = currentChain.[index]
            Some (ip, port)
        else 
            None

    member x.GetNewChain(count: int) =
        if tryConnect() then
            getNewChain count
        else
            []

    member x.GetRandomChain(count : int) =
        if tryConnect() then
            getRandomChain count
        else
            []

    member x.Connect(chain : list<string * int * RsaPublicKey * int>) =
        if tryConnect() then
            currentChain <- chain |> List.toArray
            match builChain (List.rev chain) with
                | Choice1Of2 newClient ->
                
                    match client with
                        | Some old -> old.Disconnect()
                        | None -> ()
                
                    client <- Some newClient

                    Success()
                | Choice2Of2 e ->
                    Error e
        else
            Error "could not connect to directory"

    member x.Connect(count : int) =
        if tryConnect() then
            let c = x.GetNewChain(count)
            if List.isEmpty c then
                Error "could not get chain from directory"
            else
                x.Connect c
        else
            Error "could not connect to directory"

    member x.GetServiceAddress() =
        getService()

    member x.Disconnect() =
        match client with
            | Some c -> 
                c.Disconnect()
                client <- None
                if isConnected then
                    isConnected <- false
                    socket.Disconnect()
//                socket.Disconnect()
//                reader.Dispose()
//                writer.Dispose()
//                stream.Dispose()
//                dirClient.Close()
            | None ->
                ()

    member x.Send(m : 'a) =
        match client with
            | Some c -> c.Send m
            | None -> failwith "originator not connected"

    member x.Request(m : 'a) : Async<Response> =
        async {
            match client with
                | Some c -> 
                    let! res = c.Request m
                    match res with
                        | Response.Exception e ->
                            match x.Connect(3) with
                                | Success() -> return! x.Request(m)
                                | Error e -> return Response.Exception e
                        | res ->
                            return res
                | None -> 
                    return Response.Exception "originator not connected"
        }

    member x.IsConnected =
        client.IsSome


