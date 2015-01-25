namespace Matrjoshka

open System
open System.IO
open System.Threading
open System.Net.Sockets
open Nessos.FsPickler


/// <summary>
/// PlainSocket implements ISocket and sends/receives data
/// in plain-text. It uses FSPickler for serialization/deserializtation.
/// and effectively sends/receives Base64-encoded strings.
/// </summary>
type PlainSocket() =
    static let timeout = 10000
    static let pickler = FsPickler.CreateBinary(true)
    let mutable client : TcpClient = null
    let mutable stream : NetworkStream = null
    let mutable writer : StreamWriter = null
    let mutable reader : StreamReader = null 

    let send (data : 'a) =
        if client <> null then
            // serialize the data (if not already a byte[])
            let arr = 
                match data :> obj with
                    | :? array<byte> as data -> data
                    | _ -> pickler.Pickle(data)

            try
                // send a base64 representation of the data
                writer.WriteLine(Convert.ToBase64String(arr))
                writer.Flush()
            with _ ->
                ()
        else
            failwith "client disconnected"

    let receiveAsync() : Async<'a> =
        if client <> null then
            async {
                try
                    // read a base64 string from the input
                    let! line = Async.AwaitTask <| reader.ReadLineAsync()

                    if line = null then
                        if typeof<'a> = typeof<Response> then
                            return Response.Exception "remote closed the connection" :> obj |> unbox
                        elif typeof<'a> = typeof<byte[]> then
                            let arr = pickler.Pickle (Response.Exception "remote closed the connection")
                            return arr :> obj |> unbox
                        else
                            return failwith "remote closed the connection"
                    else
                        // convert it to a byte[]
                        let arr = Convert.FromBase64String(line)

                        // deserialize the data (if not wanting a byte[])
                        if typeof<'a> = typeof<byte[]> then
                            return arr :> obj |> unbox
                        else
                            return pickler.UnPickle arr
                with e ->
                    if typeof<'a> = typeof<Response> then
                        return Response.Exception "remote closed the connection" :> obj |> unbox
                    elif typeof<'a> = typeof<byte[]> then
                        let arr = pickler.Pickle (Response.Exception "remote closed the connection")
                        return arr :> obj |> unbox
                    else
                        return failwith "remote closed the connection"
            }
        else
            failwith "client disconnected"

    let receive() : 'a =
        Async.RunSynchronously (receiveAsync(), timeout)

    let closeOldConnection() =
        let oldClient = Interlocked.Exchange(&client, null)
        if oldClient <> null then
            writer.Dispose()
            reader.Dispose()
            oldClient.Close()
            stream.Dispose()
            stream <- null

    let connect (remote : string) (port : int) =
        // close the old connection (if one exists)
        closeOldConnection()

        // establish the new connection
        client <- new TcpClient(remote, port, NoDelay = true)
        stream <- client.GetStream()
        writer <- new StreamWriter(stream)
        reader <- new StreamReader(stream)

    member x.Connect(remote : string, port : int) =
        connect remote port

    member x.Disconnect() =
        closeOldConnection()

    member x.Send(value : 'a) =
        send value

    member x.Receive() = receiveAsync()

    member x.Request(request : 'a) : Async<'b> =
        send request
        receiveAsync()


    interface ISocket with
        member x.IsConnected = client <> null && client.Connected
        member x.Disconnect() = x.Disconnect()
        member x.Send v = x.Send v
        member x.Receive() = x.Receive()
        member x.Request r = x.Request r