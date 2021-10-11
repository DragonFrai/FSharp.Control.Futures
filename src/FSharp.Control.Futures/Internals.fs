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
