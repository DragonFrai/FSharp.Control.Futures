namespace FSharp.Control.Futures.Internals

open System.Diagnostics


[<AutoOpen>]
module Utils =

    let inline internal ( ^ ) f x = f x

    let inline refEq (a: obj) (b: obj) = obj.ReferenceEquals(a, b)
    let inline nullObj<'a when 'a : not struct> = Unchecked.defaultof<'a>
    let inline isNull<'a when 'a : not struct> (x: 'a) = refEq x null
    let inline isNotNull<'a when 'a : not struct> (x: 'a) = not (isNull x)

    let inline unreachable () =
        raise (UnreachableException())


type [<Struct>] ExnResult<'a> =
    val value: 'a
    val ex: exn
    new(value: 'a, ex: exn) = { value = value; ex = ex }
    static member inline Ok(v) = ExnResult(v, Unchecked.defaultof<_>)
    static member inline Exn(e) = ExnResult(Unchecked.defaultof<_>, e)
    member inline this.Value = if isNull this.ex then this.value else raise this.ex
