[<AutoOpen>]
module Utils

open FSharp.Control.Futures


let noCallableWaker: Waker = fun () -> invalidOp "Waker was called"
