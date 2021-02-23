module FSharp.Control.Futures.Tests.Properties.Combinators

open System
open Expecto
open FsCheck

open FSharp.Control.Futures


module Gen =

    type FunctorInt2String = FunctorInt2String of (int -> string)
    type FunctorInt2StringGen() =
        static member FunctorInt2String() =
            let genFunctorInt2String: Gen<FunctorInt2String> = gen {
                let! postfix = Gen.choose (0, 1000)
                let! infix = Gen.elements [ "_"; "-"; "~" ]
                let f i =
                    sprintf "%i%s%i" i infix postfix
                return FunctorInt2String f
            }
            Arb.fromGen genFunctorInt2String

    type FutureInt = FutureInt of Future<int>
    type FutureIntGen() =
        static member FutureInt() =
            let genFutureInt: Gen<FutureInt> = gen {
                let! i = Gen.choose (Int32.MinValue + 1, Int32.MaxValue)
                let fut = future {
                    return i
                }
                return FutureInt fut
            }
            Arb.fromGen genFutureInt

    let addToConfig (config: FsCheckConfig) =
        { config with
            arbitrary =
                typeof<FunctorInt2StringGen>
                :: typeof<FutureIntGen>
                :: config.arbitrary
        }

//    let futureIntArb =


module Expect =
    module Future =
        let equal (actual: Future<'a>) (expected: Future<'a>) (message: string) : unit =
            let actualResult = Future.runSync actual
            let expectedResult = Future.runSync expected
            Expect.equal actualResult expectedResult message


[<AutoOpen>]
module TestHelpers =
    let private config = Gen.addToConfig FsCheckConfig.defaultConfig
    let testProperty name = testPropertyWithConfig config name


let equalFuture fut1 fut2 =
    let r1 = Future.runSync fut1
    let r2 = Future.runSync fut2
    r1 = r2


[<Tests>]
let properties =
    testList "Combinator properties" [
        testProperty "map f = bind (return . f)" <| fun (Gen.FutureInt (fut: Future<int>)) (Gen.FunctorInt2String (mapping: int -> string)) ->
            let rFut1: Future<string> = Future.bind (mapping >> Future.ready) fut
            let rFut2: Future<string> = Future.map mapping fut
            equalFuture rFut1 rFut2
    ]
