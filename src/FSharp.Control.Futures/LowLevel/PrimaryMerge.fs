namespace FSharp.Control.Futures.LowLevel

open FSharp.Control.Futures


[<Struct>]
type PrimaryMerge<'a, 'b> =
    val mutable _poller1: NaiveFuture<'a>
    val mutable _poller2: NaiveFuture<'b>
    val mutable _result1: 'a
    val mutable _result2: 'b
    val mutable _resultsBits: int // bitflag: r1 = 1 | r2 = 2

    new (fut1: Future<'a>, fut2: Future<'b>) =
        { _poller1 = NaiveFuture(fut1)
          _poller2 = NaiveFuture(fut2)
          _result1 = Unchecked.defaultof<_>
          _result2 = Unchecked.defaultof<_>
          _resultsBits = 0 }

    member inline this._PutResult1(r: 'a) =
        this._result1 <- r
        this._resultsBits <- this._resultsBits ||| 0b01

    member inline this._PutResult2(r: 'b) =
        this._result2 <- r
        this._resultsBits <- this._resultsBits ||| 0b10

    member inline this._IsNoResult1 = this._resultsBits &&& 0b01 = 0
    member inline this._IsNoResult2 = this._resultsBits &&& 0b10 = 0
    member inline this._HasAllResults = this._resultsBits = 0b11

    member inline this.Poll(ctx: IContext) : NaivePoll<struct ('a * 'b)> =
        if this._IsNoResult1 then
            try
                match this._poller1.Poll(ctx) with
                | NaivePoll.Ready result -> this._PutResult1(result)
                | NaivePoll.Pending -> ()
            with ex ->
                this._poller1.Terminate()
                this._poller2.Drop()
                raise ex
        if this._IsNoResult2 then
            try
                match this._poller2.Poll(ctx) with
                | NaivePoll.Ready result -> this._PutResult2(result)
                | NaivePoll.Pending -> ()
            with ex ->
                this._poller2.Terminate()
                this._poller1.Drop()
                raise ex

        if this._HasAllResults
        then NaivePoll.Ready (struct (this._result1, this._result2))
        else NaivePoll.Pending

    member inline this.Drop() =
        // Отсутствие результата также означает, что Future должна быть не терминальна
        if this._IsNoResult1 then this._poller1.Drop()
        if this._IsNoResult2 then this._poller2.Drop()
