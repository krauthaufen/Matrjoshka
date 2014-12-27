namespace Matrjoshka

open Nessos.FsPickler

/// <summary>
/// FowardSocket decorates a given socket while
/// wrapping all sent messages with a Forward-Message
/// using the specified target/port
/// </summary>
type FowardSocket(remote : string, port : int, client : ISocket) =
    static let pickler = FsPickler.CreateBinary(true)
    
    member x.Send(m : 'a) =
        let arr = 
            match m :> obj with
                | :? array<byte> as arr -> arr
                | _ -> pickler.Pickle m

        client.Send(Forward(remote, port, arr))

    member x.Receive() =
        client.Receive()

    member x.Request(m : 'a) : Async<'b> =  
        x.Send(m)
        x.Receive()

    member x.Disconnect() =
        client.Disconnect()

    member x.IsConnected =
        client.IsConnected

    interface ISocket with
        member x.IsConnected = x.IsConnected
        member x.Disconnect() = x.Disconnect()
        member x.Send v = x.Send v
        member x.Receive() = x.Receive()
        member x.Request r = x.Request r
