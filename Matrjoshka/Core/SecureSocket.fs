namespace Matrjoshka

open System
open Nessos.FsPickler
open Matrjoshka.Cryptography

/// <summary>
/// SecureSocket decorates a Socket with a secure Aes-channel.
/// It therefore establishes the shared secret using the remote's RSA-Key
/// and a standard DiffieHellman-Handshake. The protocol therefore can be seen
/// in connect (publicKey : RsaPublicKey).
/// </summary>
type SecureSocket(client : ISocket) =
    static let pickler = FsPickler.CreateBinary(true)
    let mutable aes : Option<Aes> = None
     
    let send (data : 'a) =
        // serialize the data (if not already a byte[])
        let arr = 
            match data :> obj with
                | :? array<byte> as data -> data
                | _ -> pickler.Pickle(data)

        // encrypt the data with the AES-Instance (if present)
        let arr = 
            match aes with
                | Some aes -> Aes.encrypt aes arr
                | None -> arr

        // send a base64 representation of the data
        client.Send(arr)

    let receiveAsync() : Async<'a> =
        async {
            // if the encrypted channel has already been
            // established we expect the inner client to
            // receive only a byte[] which needs to be decrypted.
            // if the encrypted channel is not yet established we expect
            // plain messages to arrive.
            match aes with
                | Some aes ->
                    let! arr = client.Receive()
                    let arr = Aes.decrypt aes arr

                    if typeof<'a> = typeof<byte[]> then
                        return arr :> obj |> unbox
                    else
                        return pickler.UnPickle arr
                | None ->
                    return! client.Receive()
        }

    let receive() : 'a =
        Async.StartAsTask(receiveAsync()).Result

    let connect (publicKey : RsaPublicKey) =
        // create new DiffieHellman- and RSA-Providers for
        // the key handshake and encryption
        let dh = DiffieHellman.create()
        let rsa = Rsa.fromPublicKey publicKey

        // encrypt the DH-PublicKey with the server's RSA-Key
        let dhPublic = DiffieHellman.publicKey dh
        let encryptedDhPublic = Rsa.encrypt rsa dhPublic
        
        // the RSA-Provider is no longer needed so destroy it
        Rsa.destroy rsa


        // send the connect-message to the server
        send (Connect encryptedDhPublic)

        try
            // wait for the server to respond
            match receive() with
                | Accept(serverDhPublic, iv, hash) ->
                    // if the server accepted the connection derive the symmetric encryption key
                    // and destroy the DiffieHellman-Provider
                    let aesKey = DiffieHellman.deriveKey dh serverDhPublic
                    DiffieHellman.destroy dh

                    // compute a hash for the derived symmetric key
                    let myHash = Sha.hash aesKey
                
                    // validate the hash using the server's hash from the response
                    if hash <> myHash then
                        // if the hashes do not match the server has derived a different symmetric key
                        // so close the connection and return an error
                        Error "could not establish a shared secret with server since the key-hashes did not match"
                    else
                        // if the hashes match the server has derived the same symmetric key and
                        // we can safely establish a AES connection
                
                        aes <- Some (Aes.create aesKey iv)
                        Success
                
                | Data _ ->
                    Error "server responded with data (expected Accept/Deny)"

                | Exception e ->
                    Error (sprintf "server encountered an error: %A" e)

                | Deny reason ->
                    // if the server denied the connection close it and return an error containing the
                    // server's reason
                    Error (sprintf "could not connect to server: %A" reason)

        with :? TimeoutException as e ->
            Error "timeout"

    member x.Connect(publicKey : RsaPublicKey) =
        connect publicKey

    member x.Disconnect() =
        client.Disconnect()

    member x.Send(value : 'a) =
        send value

    member x.Receive() : Async<'a> =
        receiveAsync()

    member x.Request(request : 'a) : Async<'b> =
        send request
        receiveAsync()

    interface ISocket with
        member x.IsConnected = client.IsConnected
        member x.Disconnect() = x.Disconnect()
        member x.Send v = x.Send v
        member x.Receive() = x.Receive()
        member x.Request r = x.Request r
