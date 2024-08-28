namespace FSharp.Control.Futures.Internals

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


/// Утилита автоматически обрабатывающая Transit от опрашиваемой футуры.
/// На данный момент, один из бонусов -- обработка переходов в терминальное состояние
/// для завершения с результатом или исключением и отмены.
/// (TODO: если try без фактического исключения не абсолютно бесплатен, есть смысл убрать его отсюда)
[<Struct; NoComparison; NoEquality>]
type NaiveFuture<'a> =
    val mutable public Internal: Future<'a>
    new(fut: Future<'a>) = { Internal = fut }

    member inline this.IsTerminated: bool = isNull this.Internal
    member inline this.Terminate() : unit = this.Internal <- nullObj

    member inline this.Poll(ctx: IContext) : NaivePoll<'a> =
        let mutable result = Unchecked.defaultof<_>
        let mutable doLoop = true
        while doLoop do
            let poll =
                try this.Internal.Poll(ctx)
                with e -> this.Terminate(); reraise ()

            match poll with
            | Poll.Ready r ->
                this.Internal <- nullObj
                doLoop <- false
                result <- NaivePoll.Ready r
            | Poll.Pending ->
                doLoop <- false
                result <- NaivePoll.Pending
            | Poll.Transit transitTo ->
                this.Internal <- transitTo
        result

    member inline this.Drop() : unit =
        // Set null before drop call, because drop can throw exception
        let internal' = this.Internal
        this.Internal <- nullObj
        internal'.Drop()
