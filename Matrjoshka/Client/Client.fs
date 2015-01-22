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
    
    let dirClient = new TcpClient(directory, port)
    let stream = dirClient.GetStream()

    let reader = new StreamReader(stream)
    let writer = new StreamWriter(stream)

    let getChain(req: int->DirectoryRequest, l:int) =
        let r = req l
        let data = pickler.Pickle r
        let str = Convert.ToBase64String data
        writer.WriteLine str
        writer.Flush()

        let reply = reader.ReadLine()
        let reply = Convert.FromBase64String reply

        match pickler.UnPickle reply with
            | Nodes list ->
                list
            | InsufficientRelays available ->
                failwithf "could not get chain of length %d (%d relays available)" l available

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
                    | Success -> Choice1Of2 sec
                    | Error e -> Choice2Of2 e

            | (remote, port, key, useCount)::rest ->
                match builChain rest with
                    | Choice2Of2 e -> Choice2Of2 e
                    | Choice1Of2 inner ->
                        let fw = FowardSocket(remote, port, inner)

                        let sec = SecureSocket(fw)
                        match sec.Connect key with
                            | Success -> Choice1Of2 sec
                            | Error e -> Choice2Of2 e

            | [] ->
                failwith "cannot establish empty chain"

    let mutable client : Option<SecureSocket> = None


    member x.GetNewChain(count: int) =
        getNewChain count

    member x.GetRandomChain(count : int) =
        getRandomChain count

    member x.Connect(chain : list<string * int * RsaPublicKey * int>) =

        match builChain (List.rev chain) with
            | Choice1Of2 newClient ->
                
                match client with
                    | Some old -> old.Disconnect()
                    | None -> ()
                
                client <- Some newClient

                Success
            | Choice2Of2 e ->
                Error e

    member x.Connect(count : int) =
        let c = x.GetNewChain(count)
        x.Connect c

    member x.Disconnect() =
        match client with
            | Some c -> 
                c.Disconnect()
                client <- None
                reader.Dispose()
                writer.Dispose()
                stream.Dispose()
                dirClient.Close()
            | None ->
                ()

    member x.Send(m : 'a) =
        match client with
            | Some c -> c.Send m
            | None -> failwith "originator not connected"

    member x.Receive() =
        match client with
            | Some c -> c.Receive()
            | None -> failwith "originator not connected"

    member x.Request(m : 'a) =
        match client with
            | Some c -> c.Request m
            | None -> failwith "originator not connected"

    member x.IsConnected =
        client.IsSome

    interface ISocket with
        member x.IsConnected = x.IsConnected
        member x.Disconnect() = x.Disconnect()
        member x.Send v = x.Send v
        member x.Receive() = x.Receive()
        member x.Request r = x.Request r

