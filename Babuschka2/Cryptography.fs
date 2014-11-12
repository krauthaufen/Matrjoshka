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
open Mono.Security.Cryptography

type DiffieHellmanPublicKey = byte[]
type RsaPublicKey = byte[]

type DiffieHellman = private { dh : Mono.Security.Cryptography.DiffieHellman }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DiffieHellman =
    let create() =
        let dh = new Mono.Security.Cryptography.DiffieHellmanManaged(1024,140, DHKeyGeneration.Random)

        { dh = dh }

    let publicKey (dh : DiffieHellman) =
        dh.dh.CreateKeyExchange()

    let deriveKey (dh : DiffieHellman) (publicKey : byte[]) =
        dh.dh.DecryptKeyExchange publicKey


    let destroy (dh : DiffieHellman) =
        dh.dh.Dispose()


type Rsa = private { rsa : RSACryptoServiceProvider; canEncrypt : bool; canDecrypt : bool }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rsa =
    let create() =
        { rsa = new RSACryptoServiceProvider(1024); canEncrypt = true; canDecrypt = true }

    let destroy (rsa : Rsa) =
        rsa.rsa.Dispose()

    let publicKey (rsa : Rsa) =
        let p = rsa.rsa.ExportParameters(false)
        printfn "%A" p.Exponent.Length
        Array.concat [p.Exponent;p.Modulus]

    let fromPublicKey (key : byte[]) =
        let exp = Array.sub key 0 1
        let modulus = Array.sub key 1 (key.Length - 1)

        let p = RSAParameters(Exponent = exp, Modulus = modulus)
        let rsa = new RSACryptoServiceProvider(2048)
        rsa.ImportParameters(p)
        { rsa = rsa; canEncrypt = true; canDecrypt = false }

    let encrypt (rsa : Rsa) (data : byte[]) =
        if rsa.canEncrypt then
            let maxSize = 100

            if data.Length > maxSize then
                [0..maxSize..data.Length-1] |> List.map (fun start -> 
                    let size = 
                        if start + maxSize > data.Length then data.Length - start
                        else maxSize
                    printfn "enc: %A" size
                    let res = rsa.rsa.Encrypt(Array.sub data start size, false)

                    res
                ) |> Array.concat
            else
                rsa.rsa.Encrypt(data, false)
        else
            failwith "cannot encrypt since Rsa does not posess the public key"

    let decrypt (rsa : Rsa) (data : byte[]) =
        if rsa.canDecrypt then
            let maxSize = 128

            if data.Length > maxSize then
                [0..maxSize..data.Length-1] |> List.map (fun start -> 
                    let size = 
                        if start + maxSize > data.Length then data.Length - start
                        else maxSize

                    let dec = rsa.rsa.Decrypt(Array.sub data start size, false)
                    printfn "dec: %A" dec.Length
                    dec
                ) |> Array.concat
            else
                rsa.rsa.Decrypt(data, false)
        else
            failwith "cannot decrypt since Rsa does not posess the private key"

type Aes = private { aes : AesManaged }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Aes =
    let createNew (key : byte[]) =
        let aes = new AesManaged()
        printfn "aes: %A" (aes.LegalKeySizes |> Array.map (fun k -> k.MinSize, k.MaxSize))
        printfn "key: %A" key.Length
        aes.Key <- key
        aes.GenerateIV()
        { aes = aes }

    let create (key : byte[]) (iv : byte[]) =
        let aes = new AesManaged()
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
