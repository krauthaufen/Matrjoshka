namespace Matrjoshka

open Matrjoshka.Cryptography

/// <summary>
/// Error is a simple union-type either being an error with
/// a message or Success
/// </summary>
type Error = Success | Error of string

/// <summary>
/// Message represents all message-types that can
/// be sent by a client. 
///
/// + Connect contains a RSA-encrypted DH handshake
/// + Forward consists of a target (with its port) and
///   payload-data which shall be forwarded
/// + Command consists of a string-command and some
///   serializable object (can be used for anything)
/// + Request represents a HTTP-Request knowing its target/port
///   and the binary data which shall be sent to the remote
/// </summary>
type Message =
    | Connect of byte[]
    | Forward of string * int * byte[]
    | Command of string * obj
    | Request of string * int * byte[]

type Response =
    | Accept of DiffieHellmanPublicKey * byte[] * byte[]
    | Deny of string
    | Exception of string
    | Data of byte[]

type Ping =
    | Alive of string * int * RsaPublicKey
    | Shutdown of string * int

type DirectoryRequest =
    | Random of int
    | All
    
type DirectoryResponse =
    | Nodes of list<string * int * RsaPublicKey>
    | InsufficientRelays of int