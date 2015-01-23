namespace Matrjoshka.Cryptography


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
open Mono.Security
open Mono.Security.Cryptography

type DiffieHellmanPublicKey = byte[]
type RsaPublicKey = byte[]

#if WINDOWS
type RSAManaged = System.Security.Cryptography.RSACryptoServiceProvider
type DiffieHellmanManaged = System.Security.Cryptography.ECDiffieHellmanCng
#endif

type Rsa = private { rsa : RSAManaged; canEncrypt : bool; canDecrypt : bool }
type Aes = private { aes : AesManaged }
type DiffieHellman = private { mutable dh : DiffieHellmanManaged }




[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module DiffieHellman =
    let private pickler = FsPickler.CreateBinary(true)
    type Handshake = { g : byte[]; p : byte[]; exchange : byte[] }

    let create() =
        { dh = new System.Security.Cryptography.ECDiffieHellmanCng(521) }

    let publicKey (dh : DiffieHellman) =
        //let exchange = dh.dh.CreateKeyExchange()
        //let parameters = dh.dh.ExportParameters(false)
        //let handshake = { g = parameters.G; p = parameters.P; exchange = exchange }
        //pickler.Pickle(handshake)
        #if WINDOWS
        dh.dh.PublicKey.ToByteArray()
        #else
        dh.dh.CreateKeyExchange()
        #endif

    let deriveKey (dh : DiffieHellman) (publicKey : byte[]) =
        //let handshake : Handshake = pickler.UnPickle(publicKey)
        //dh.dh.ImportParameters(DHParameters(G = handshake.g, P = handshake.p))
        #if WINDOWS
        dh.dh.DeriveKeyMaterial(ECDiffieHellmanCngPublicKey.FromByteArray(publicKey, CngKeyBlobFormat.GenericPublicBlob))
        #else
        dh.dh.DecryptKeyExchange(publicKey)
        #endif
        //let key = ECDiffieHellmanCngPublicKey.FromByteArray(publicKey, CngKeyBlobFormat.GenericPublicBlob)
        //dh.dh.DeriveKeyMaterial(key)

    let destroy (dh : DiffieHellman) =
        dh.dh.Dispose()

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Rsa =
    let private bits = 2048

    let create() =
        { rsa = new RSAManaged(bits); canEncrypt = true; canDecrypt = true }

    let destroy (rsa : Rsa) =
        rsa.rsa.Dispose()

    let publicKey (rsa : Rsa) =
        let p = rsa.rsa.ExportParameters(false)
        Array.concat [BitConverter.GetBytes(p.Exponent.Length); p.Exponent;p.Modulus]

    let fromPublicKey (key : byte[]) =
        let length = BitConverter.ToInt32(key, 0)
        let key = Array.sub key 4 (key.Length - 4)
        let exp = Array.sub key 0 length
        let modulus = Array.sub key length (key.Length - length)

        let p = RSAParameters(Exponent = exp, Modulus = modulus)
        let rsa = new RSAManaged(bits)
        rsa.ImportParameters(p)
        { rsa = rsa; canEncrypt = true; canDecrypt = false }

    let encrypt (rsa : Rsa) (data : byte[]) =
        //data
        if rsa.canEncrypt then
            #if WINDOWS
            rsa.rsa.Encrypt(data, false)
            #else
            rsa.rsa.EncryptValue(data)
            #endif
        else
            failwith "cannot encrypt since Rsa does not posess the public key"

    let decrypt (rsa : Rsa) (data : byte[]) =
        //data
        if rsa.canDecrypt then  
            #if WINDOWS
            rsa.rsa.Decrypt(data, false)
            #else
            rsa.rsa.DecryptValue(data)
            #endif
        else
            failwith "cannot decrypt since Rsa does not posess the private key"

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Aes =
    let createNew (key : byte[]) =
        let aes = new AesManaged()
        aes.Key <- Array.sub key 0 (aes.KeySize / 8)
        aes.GenerateIV()
        { aes = aes }

    let create (key : byte[]) (iv : byte[]) =
        let aes = new AesManaged()
        aes.Key <- Array.sub key 0 (aes.KeySize / 8)
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
