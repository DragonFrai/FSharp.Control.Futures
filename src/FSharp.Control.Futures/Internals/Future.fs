namespace FSharp.Control.Futures.Internals

open FSharp.Control.Futures


/// <summary>
/// Future low level functions
/// </summary>
[<RequireQualifiedAccess>]
module Future =

    let inline drop (fut: Future<'a>) = fut.Drop()

    let inline poll context (fut: Future<'a>) = fut.Poll(context)

    let inline create ([<InlineIfLambda>] poll) ([<InlineIfLambda>] drop) =
        { new Future<_> with
            member _.Poll(ctx) = poll ctx
            member _.Drop() = drop () }
