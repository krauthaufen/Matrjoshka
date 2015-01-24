namespace Matrjoshka

open System

//
//type Error<'a> = Success of 'a | Error of string

type ChainNodeHandle = { privateAddress : string; publicAddress : string; port : int; shutdown : unit -> Async<unit> }


[<AbstractClass>]
type ChainPool() =

    /// <summary>
    /// releases all resources associated with the chain pool
    /// </summary>
    abstract member Dispose : unit -> unit

    /// <summary>
    /// start the given number of chain nodes
    /// </summary>
    abstract member StartChainAsync : int -> Async<list<ChainNodeHandle>>

    /// <summary>
    /// starts a new instance running the service
    /// </summary>
    abstract member StartServiceAsync : unit -> Async<ChainNodeHandle>


    interface IDisposable with
        member x.Dispose() = x.Dispose()