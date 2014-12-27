namespace Matrjoshka

open System


type Error<'a> = Success of 'a | Error of string

type ChainNodeHandle = { privateAddress : string; publicAddress : string; port : int; shutdown : unit -> Async<unit> }


[<AbstractClass>]
type ChainPool() =
    abstract member Dispose : unit -> unit
    abstract member StartChainAsync : int -> Async<list<ChainNodeHandle>>
    
    interface IDisposable with
        member x.Dispose() = x.Dispose()