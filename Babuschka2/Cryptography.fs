namespace Babuschka.Cryptography

open System
open System.Net
open System.IO
open System.Net.Sockets
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters.Binary
open System.Collections.Concurrent
open System.Threading
open System.Threading.Tasks
open System.Security.Cryptography
open System.Net.Security
open Nessos.FsPickler
open Nessos.FsPickler.Combinators

type DiffieHellmanPublicKey = byte[]
type RsaPublicKey = byte[]

type DiffieHellman = private { dh : ECDiffieHellmanCng }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DiffieHellman =
    let create() =
        { dh = new ECDiffieHellmanCng() }

    let publicKey (dh : DiffieHellman) =
        dh.dh.PublicKey.ToByteArray()

    let deriveKey (dh : DiffieHellman) (publicKey : byte[]) =
        let key = ECDiffieHellmanCngPublicKey.FromByteArray(publicKey, CngKeyBlobFormat.GenericPublicBlob)
        dh.dh.DeriveKeyMaterial(key)

    let destroy (dh : DiffieHellman) =
        dh.dh.Dispose()


type Rsa = private { rsa : RSACryptoServiceProvider; canEncrypt : bool; canDecrypt : bool }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rsa =
    let create() =
        { rsa = new RSACryptoServiceProvider(2048); canEncrypt = true; canDecrypt = true }

    let destroy (rsa : Rsa) =
        rsa.rsa.Dispose()

    let publicKey (rsa : Rsa) =
        let p = rsa.rsa.ExportParameters(false)
        Array.concat [p.Exponent;p.Modulus]

    let fromPublicKey (key : byte[]) =
        let exp = Array.sub key 0 3
        let modulus = Array.sub key 3 (key.Length - 3)

        let p = RSAParameters(Exponent = exp, Modulus = modulus)
        let rsa = new RSACryptoServiceProvider(2048)
        rsa.ImportParameters(p)
        { rsa = rsa; canEncrypt = true; canDecrypt = false }

    let encrypt (rsa : Rsa) (data : byte[]) =
        if rsa.canEncrypt then
            rsa.rsa.Encrypt(data, true)
        else
            failwith "cannot encrypt since Rsa does not posess the public key"

    let decrypt (rsa : Rsa) (data : byte[]) =
        if rsa.canDecrypt then
            rsa.rsa.Decrypt(data, true)
        else
            failwith "cannot decrypt since Rsa does not posess the private key"

type Aes = private { aes : AesCryptoServiceProvider }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Aes =
    let createNew (key : byte[]) =
        let aes = new AesCryptoServiceProvider()
        aes.Key <- key
        aes.GenerateIV()
        { aes = aes }

    let create (key : byte[]) (iv : byte[]) =
        let aes = new AesCryptoServiceProvider()
        aes.Key <- key
        aes.IV <- iv
        { aes = aes }

    let key (aes : Aes) =
        aes.aes.Key

    let initialVector (aes : Aes) =
        aes.aes.IV


    let destroy (aes : Aes) =
        aes.aes.Dispose()

    let encrypt (aes : Aes) (data : byte[]) =
        use ms = new MemoryStream()
        use c = new CryptoStream(ms, aes.aes.CreateEncryptor(), CryptoStreamMode.Write)
        c.Write(data, 0, data.Length)
        c.FlushFinalBlock()
        ms.ToArray()

    let decrypt (aes : Aes) (data : byte[]) =
        use ms = new MemoryStream(data)
        use c = new CryptoStream(ms, aes.aes.CreateDecryptor(), CryptoStreamMode.Read)
        use result = new MemoryStream()

        let buffer = Array.zeroCreate 1024
        while ms.Position < ms.Length do
            let r = c.Read(buffer, 0, buffer.Length)
            result.Write(buffer, 0, r)

        result.ToArray()

module Sha =
    let private sha = SHA512.Create()

    let hash (data : byte[]) =
        sha.ComputeHash(data)
