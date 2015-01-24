namespace Matrjoshka

open Amazon.EC2.Model
open Amazon.EC2.Util
open Amazon.EC2
open Amazon
open System.IO
open System.Threading
open System.Threading.Tasks
open System

module Sim =

    let createChainPool (basePort : int) (directoryPort : int) (servicePort : int) =
        let currentPort = ref basePort

        { new ChainPool() with

            member x.StartChainAsync (count : int) =
                async {
                    let instances = 
                        [ for _ in 0..count-1 do
                            let port = Interlocked.Increment(&currentPort.contents)

                            let node = Relay("127.0.0.1", directoryPort, sprintf "c%d" port, port)

                            node.Start() |> ignore

                            yield { privateAddress = "127.0.0.1"; publicAddress = "127.0.0.1"; port = port; shutdown = fun () -> async { return node.Stop() } }
                        ]

                    return instances
                }

            member x.StartServiceAsync () =
                async {
                    let s = Service(servicePort)
                    s.Start()
                    return { privateAddress = "localhost"; publicAddress = "localhost"; port = servicePort; shutdown = fun () -> async { return () } }
                }

            member x.Dispose() = ()        
        }

