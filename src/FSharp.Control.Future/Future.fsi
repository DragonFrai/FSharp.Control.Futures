namespace FSharp.Control.Future

type Future<'a> = FSharp.Control.Future.Core.Future<'a>

module Future =

    val ready: value: 'a -> Future<'a>

    val bind: binder: ('a -> Future<'b>) -> fut: Future<'a> -> Future<'b>

    val map: mapping: ('a -> 'b) -> fut: Future<'a> -> Future<'b>

    val never: unit -> Future<'a>

    val ignore: future: Future<'a> -> Future<unit>

//    val merge: fut1: Future<'a> -> fut2: Future<'b> -> Future<'a * 'b>

    val join: fut: Future<Future<'a>> -> Future<'a>

