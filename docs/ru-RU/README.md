
# Futures

Futures это экспериментальная F# библиотека асинхронного программирования,
вдохновленная Rust трейтом Future.

## Features
- Дизайн Future позволяет отменять любые асинхронные операции
  без телодвижений со стороны программиста и CancellationToken-ов
- Модель опроса делает возможным полное отсутствие блокировок в комбинаторах
  и не требует выделения память под обратные вызовы.
- Явные точки прерывания
- Future является "холодной" (вычисление начинается только после запуска).


Библиотека предоставляет асинхронные аналоги следующих примитивов

| Асинхронная версия | Синхронная версия |
| ------------------ | ----------------- |
| IComputation<'a>   | x: 'a             |
| Future<'a>         | unit -> 'a        |
| IStreamer<'a>      | IEnumerator<'a>   |
| Stream<'a>         | IEnumerable<'a>   |

Сравнение этих типов

| Тип              | Thread-safe                     | Перезапускаем |
| ---------------- | ------------------------------- | ------------- |
| IComputation<'a> | -                               | -             |
| Future<'a>       | +<sup name="a1">[1](#f1)</sup>  | +             |
| IStreamer<'a>    | -                               | -             |
| Stream<'a>       | +<sup name="a1">[1](#f1)</sup>  | +             |

----
<a name="f1">1</a>: если замыкание внутри Thread-safe

