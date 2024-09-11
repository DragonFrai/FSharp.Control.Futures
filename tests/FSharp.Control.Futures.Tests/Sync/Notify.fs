module FSharp.Control.Futures.Tests.Sync.Notify

open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Sync
open Xunit


// [ Notify N tests ]

[<Fact>]
let ``Notify one``() =
    let n = Notify()
    let waitFTask = spawn (n.Wait())
    Assert.Equal(NaivePoll.Pending, waitFTask.Poll())
    n.Notify()
    Assert.Equal(NaivePoll.Ready (), waitFTask.Poll())

[<Fact>]
let ``Notify two``() =
    let n = Notify()

    let waitFTask1 = spawn (n.Wait())
    let waitFTask2 = spawn (n.Wait())

    Assert.Equal(NaivePoll.Pending, waitFTask1.Poll())
    Assert.Equal(NaivePoll.Pending, waitFTask2.Poll())

    n.Notify()
    Assert.Equal(NaivePoll.Ready (), waitFTask1.Poll())
    Assert.Equal(NaivePoll.Pending, waitFTask2.Poll(false))

    n.Notify()
    Assert.Equal(NaivePoll.Ready (), waitFTask2.Poll())


// [ Notify props test ]

[<Fact>]
let ``Multiple notify with waiters notify only notified number waiters``() =
    let n = Notify()

    let fTask1 = spawn (n.Wait())
    let fTask2 = spawn (n.Wait())
    let fTask3 = spawn (n.Wait())

    Assert.Equal(NaivePoll.Pending, fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())
    Assert.Equal(NaivePoll.Pending, fTask3.Poll())

    n.Notify()
    n.Notify()

    Assert.Equal(NaivePoll.Ready (), fTask1.Poll())
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())
    Assert.Equal(NaivePoll.Pending, fTask3.Poll(false))

[<Fact>]
let ``Multiple notify without waiters notify only one waiter``() =
    let n = Notify()

    let fTask1 = spawn (n.Wait())
    let fTask2 = spawn (n.Wait())

    n.Notify()
    n.Notify()

    Assert.Equal(NaivePoll.Ready (), fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())

[<Fact>]
let ``Creating Wait future not queued it to notification``() =
    let n = Notify()
    let waitFTask1 = spawn (n.Wait())
    let waitFTask2 = spawn (n.Wait())

    n.Notify()
    Assert.Equal(NaivePoll.Ready (), waitFTask2.Poll())
    Assert.Equal(NaivePoll.Pending, waitFTask1.Poll())
    n.Notify()
    Assert.Equal(NaivePoll.Ready (), waitFTask1.Poll())

// [ Drop tests ]

[<Fact>]
let ``Drop not enabled Wait future do not block queue``() =
    let n = Notify()

    let fTask1 = spawn (n.Wait())
    let fTask2 = spawn (n.Wait())

    fTask1.Drop()

    Assert.Equal(NaivePoll.Pending, fTask2.Poll())
    n.Notify()
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())

[<Fact>]
let ``Drop enabled Wait future do not block queue``() =
    let n = Notify()

    let fTask1 = spawn (n.Wait())
    let fTask2 = spawn (n.Wait())

    Assert.Equal(NaivePoll.Pending, fTask1.Poll())
    fTask1.Drop()
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())

    n.Notify()
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())

[<Fact>]
let ``Drop enabled and notified Wait future do not block queue``() =
    let n = Notify()

    let fTask1 = spawn (n.Wait())
    let fTask2 = spawn (n.Wait())

    Assert.Equal(NaivePoll.Pending, fTask1.Poll())
    Assert.Equal(NaivePoll.Pending, fTask2.Poll())

    n.Notify()
    Assert.True(fTask1.IsWaked)
    Assert.False(fTask2.IsWaked)

    fTask1.Drop()
    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())

[<Fact>]
let ``Notify after drop all``() =
    let n = Notify()

    let fTask1 = n.Wait() |> spawn

    Assert.Equal(NaivePoll.Pending, fTask1.Poll())

    n.NotifyWaiters()
    n.Notify()

    fTask1.Drop()

    let fTask2 = n.Wait() |> spawn

    Assert.Equal(NaivePoll.Ready (), fTask2.Poll())


(*

#[test]
fn notify_multi_notified_one() {
    let notify = Notify::new();
    let mut notified1 = spawn(async { notify.notified().await });
    let mut notified2 = spawn(async { notify.notified().await });

    // add two waiters into the queue
    assert_pending!(notified1.poll());
    assert_pending!(notified2.poll());

    // should wakeup the first one
    notify.notify_one();
    assert_ready!(notified1.poll());
    assert_pending!(notified2.poll());
}

*)
