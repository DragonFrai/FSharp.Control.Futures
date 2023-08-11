namespace FSharp.Control.Futures.Internals

open FSharp.Control.Futures
open FSharp.Control.Futures.Core


[<AutoOpen>]
type Helpers =

    // NOTE: This method has very strange call syntax:
    // ```
    // PollTransiting(^fut, ctx
    // , onReady=fun x ->
    //     doSmthOnReady x
    // , onPending=fun () ->
    //     doSmthOnPending ()
    // )
    // ```
    // but it's library only helper, so it's ok
    static member inline PollTransiting(fut: _ byref, ctx, [<InlineIfLambda>] onReady, [<InlineIfLambda>] onPending) =
        let mutable doLoop = true
        let mutable result = Unchecked.defaultof<'b>
        while doLoop do
            let p = Future.poll ctx fut
            match p with
            | Poll.Ready x ->
                result <- onReady x
                doLoop <- false
            | Poll.Pending ->
                result <- onPending ()
                doLoop <- false
            | Poll.Transit f ->
                fut <- f
        result

[<AutoOpen>]
module Helpers =

    type [<Struct; RequireQualifiedAccess>]
        NaivePoll<'a> =
        | Ready of result: 'a
        | Pending

    /// Утилита автоматически обрабатывающая переход в терминальное состояние для завершения и отмены.
    /// НЕ обрабатывает переход в терминальное состояние при исключении
    [<Struct>]
    type NaivePoller<'a> =
        val mutable public Internal: Future<'a>
        new(fut: Future<'a>) = { Internal = fut }

        member inline this.IsTerminated: bool = isNull this.Internal
        member inline this.Terminate() : unit = this.Internal <- nullObj

        member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
            let mutable result = Unchecked.defaultof<_>
            let mutable makePoll = true
            while makePoll do
                match this.Internal.Poll(ctx) with
                | Poll.Ready r ->
                    result <- NaivePoll.Ready r
                    makePoll <- false
                    this.Internal <- nullObj
                | Poll.Pending ->
                    result <- NaivePoll.Pending
                    makePoll <- false
                | Poll.Transit transitTo ->
                    this.Internal <- transitTo
            result

        member inline this.Cancel() : unit =
            this.Internal.Cancel()
            this.Internal <- nullObj

    [<Struct>]
    type StructuralMerge<'a, 'b> =
        val mutable _poller1: NaivePoller<'a>
        val mutable _poller2: NaivePoller<'b>
        val mutable _result1: 'a
        val mutable _result2: 'b
        val mutable _resultsBits: int // bitflag: r1 = 1 | r2 = 2

        new (fut1: Future<'a>, fut2: Future<'b>) =
            { _poller1 = NaivePoller(fut1)
              _poller2 = NaivePoller(fut2)
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
                    this._poller2.Cancel()
                    raise ex
            if this._IsNoResult2 then
                try
                    match this._poller2.Poll(ctx) with
                    | NaivePoll.Ready result -> this._PutResult2(result)
                    | NaivePoll.Pending -> ()
                with ex ->
                    this._poller2.Terminate()
                    this._poller1.Cancel()
                    raise ex

            if this._HasAllResults
            then NaivePoll.Ready (struct (this._result1, this._result2))
            else NaivePoll.Pending

        member inline this.Cancel() =
            // Отсутствие результата также означает, что Future должна быть не терминальна
            if this._IsNoResult1 then this._poller1.Cancel()
            if this._IsNoResult2 then this._poller2.Cancel()

    let inline pollTransiting
        (fut: Future<'a>) (ctx: IContext)
        ([<InlineIfLambda>] onReady: 'a -> 'b)
        ([<InlineIfLambda>] onPending: unit -> 'b)
        ([<InlineIfLambda>] onTransitAction: Future<'a> -> unit)
        : 'b =
        let rec pollTransiting fut =
            let p = Future.poll ctx fut
            match p with
            | Poll.Ready x -> onReady x
            | Poll.Pending -> onPending ()
            | Poll.Transit f ->
                onTransitAction f
                pollTransiting f
        pollTransiting fut

    let inline cancelIfNotNull (fut: Future<'a>) =
        if isNotNull fut then fut.Cancel()
