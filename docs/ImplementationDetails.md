# Implementation details v0.1.*

## Type `Future`

First, let's take a look at the basic types

```fs
type Poll<'a> =
    | Ready of 'a
    | Pending

type Waker = unit -> unit

type Future<'a> = Future of (Waker -> Poll<'a>)
```

`Future <'a>` is a memoized "unit of calculation". Or it can be called a non-resettable state machine whose purpose is to get an instance of `'a`.

The whole mechanism for organizing asynchronous computation based on `Future` is reduced to *polling* the `Future` itself by calling \[*`Waker -> Poll <'a>`* (hereinafter `poll`)\].

The result of `poll` is the current state of `Future`, namely *in process `Pending`* or *ready `Ready x`*.

If `Future` does not perform asynchronous work and can return the result immediately (perhaps with some work), then the first poll should return `Ready`. Otherwise, the `Future` must remember the passed `Waker` and call it when the asynchronous work completes so that the scheduler * wakes it up from waiting for this work.

> **Important**: `Future` must always *wake up* using the **last** `Waker` passed to it. However, it also does not need to be polled again before calling `Waker`.


## Expected invariants

1. A `poll` call of an instance of `Future` always occurs at the same time by no more than one thread.
2. A `Future` is only polled after the last `Waker` passed to it has been called.
3. > This invariant is in the process of discussion and may change, the main one is 3.1.

    3.1. `Future` can call `Waker` multiple times (but only the last known to it). Therefore, if `Future` executes 2 or more asynchronous jobs in parallel, then additional synchronization should be imposed on the saved `Waker`, if it can be called several times.

    3.2. `Future` can call `Waker` only once (and only the last known to it).
