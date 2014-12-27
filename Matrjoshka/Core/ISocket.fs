namespace Matrjoshka

type ISocket =
    abstract member IsConnected : bool
    abstract member Disconnect : unit -> unit
    abstract member Send : 'a -> unit
    abstract member Receive : unit -> Async<'a>
    abstract member Request : 'a -> Async<'b>
