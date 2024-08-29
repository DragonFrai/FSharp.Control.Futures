namespace FSharp.Control.Futures.Mailbox

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


exception MailboxMultipleReceiveAtSameTimeException

/// <summary> Multiple Producer Single Consumer (MPSC) synchronisation channel designed like F# MailboxProcessor </summary>
/// <remarks> It is planned that this will be the only type of channels within the framework of the usual Future.
/// More specialized channels will be included in FSharp.Control.Futures.Streams </remarks>
type [<Sealed>] Mailbox<'a> =
    val internal spinLock: SpinLock
    val mutable internal msgQueue: 'a list
    val mutable internal receiver: MailboxReceiver<'a>
    val mutable internal context: IContext

    new () = { spinLock = SpinLock(false); msgQueue = []; receiver = nullObj; context = nullObj }

    member this.Receive(): Future<'a> =
        if isNotNull this.receiver then raise MailboxMultipleReceiveAtSameTimeException
        let rx = MailboxReceiver<'a>(this)
        this.receiver

    member this.Post(msg: 'a): unit =
        OnceVarImpl.MailboxPost(this, msg)

    member this.PostWithReply<'r>(msgBuilder: OneShot<'r> -> 'a): Future<'r> = future {
        let reply = OneShot()
        let msg = msgBuilder reply
        this.Post(msg)
        let! r = reply
        return r
    }

and internal MailboxReceiver<'a> =
    val mutable internal mailbox: Mailbox<'a>

    internal new (mailbox: Mailbox<'a>) = { mailbox = mailbox }

    interface Future<'a> with
        member this.Poll(ctx: IContext): Poll<'a> =
            OnceVarImpl.ReceiverPoll(this.mailbox, this, ctx) |> NaivePoll.toPoll

        member this.Drop() =
            OnceVarImpl.ReceiverDrop(this.mailbox, this)

// TODO: Make inline
and [<RequireQualifiedAccess>] OnceVarImpl =

    static member internal ReceiverPoll(inbox: Mailbox<'a>, receiver: MailboxReceiver<'a>, ctx: IContext): NaivePoll<'a> =
        let mutable hasLock = false
        inbox.spinLock.Enter(&hasLock)
        assert (refEq inbox.receiver receiver)

        match inbox.msgQueue with
        | head :: tail ->
            inbox.msgQueue <- tail
            inbox.receiver <- nullObj
            receiver.mailbox <- nullObj
            if isNotNull inbox.context then inbox.context <- nullObj
            if hasLock then inbox.spinLock.Exit()
            NaivePoll.Ready head
        | [] ->
            if isNull inbox.context then inbox.context <- ctx
            if hasLock then inbox.spinLock.Exit()
            NaivePoll.Pending

    static member internal MailboxPost(inbox: Mailbox<'a>, msg: 'a) : unit =
        let mutable hasLock = false
        inbox.spinLock.Enter(&hasLock)
        let msgQueueWasEmpty = inbox.msgQueue.IsEmpty
        inbox.msgQueue <- inbox.msgQueue @ [msg]
        if msgQueueWasEmpty && (isNotNull inbox.context) then
            inbox.context.Wake()
        if hasLock then inbox.spinLock.Exit()
        ()

    static member internal ReceiverDrop(inbox: Mailbox<'a>, receiver: MailboxReceiver<'a>) : unit =
        let mutable hasLock = false
        inbox.spinLock.Enter(&hasLock)
        assert (refEq inbox.receiver receiver)
        inbox.receiver <- nullObj
        receiver.mailbox <- nullObj
        if isNotNull inbox.context then inbox.context <- nullObj
        if hasLock then inbox.spinLock.Exit()
        ()
