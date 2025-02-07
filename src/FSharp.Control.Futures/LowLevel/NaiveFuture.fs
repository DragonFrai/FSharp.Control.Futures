namespace FSharp.Control.Futures.LowLevel

open FSharp.Control.Futures


type [<Struct; RequireQualifiedAccess>]
    NaivePoll<'a> =
    | Ready of value: 'a
    | Pending

module NaivePoll =
    let inline toPoll (naivePoll: NaivePoll<'a>) : Poll<'a> =
        match naivePoll with
        | NaivePoll.Ready result -> Poll.Ready result
        | NaivePoll.Pending -> Poll.Pending

    let inline toPollExn (naivePoll: NaivePoll<ExnResult<'a>>) : Poll<'a> =
        match naivePoll with
        | NaivePoll.Ready result -> Poll.Ready result.Value
        | NaivePoll.Pending -> Poll.Pending

    let inline isReady (naivePoll: NaivePoll<'a>) : bool =
        match naivePoll with
        | NaivePoll.Ready _ -> true
        | _ -> false

    let inline isPending (naivePoll: NaivePoll<'a>) : bool =
        match naivePoll with
        | NaivePoll.Pending -> true
        | _ -> false

/// <summary>
/// A wrapper that automatically handles Transit from inner Future.
/// </summary>
/// (TODO: если try без фактического исключения не абсолютно бесплатен, есть смысл убрать его отсюда)
[<Struct; NoComparison; NoEquality>]
type NaiveFuture<'a> =
    val mutable public Inner: Future<'a>
    new(fut: Future<'a>) = { Inner = fut }

    member inline this.IsNull: bool = isNull this.Inner
    member inline this.IsNotNull: bool = isNotNull this.Inner
    member inline this.SetNull() : unit = this.Inner <- nullObj
    static member inline Null : NaiveFuture<'a> = NaiveFuture(nullObj)

    member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
        let mutable result = Unchecked.defaultof<_>
        let mutable doLoop = true
        while doLoop do
            let poll =
                try this.Inner.Poll(ctx)
                with e ->
                    this.Inner <- nullObj
                    reraise ()
            match poll with
            | Poll.Ready r ->
                this.Inner <- nullObj
                doLoop <- false
                result <- NaivePoll.Ready r
            | Poll.Pending ->
                doLoop <- false
                result <- NaivePoll.Pending
            | Poll.Transit transitTo ->
                this.Inner <- transitTo
        result

    member inline this.Drop() : unit =
        // Set null before drop call, because drop can throw exception
        let inner' = this.Inner
        this.Inner <- nullObj
        inner'.Drop()
