
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
| ------------------ |-------------------|
| Future<'a>         | unit -> 'a        |
| Stream<'a>         | IEnumerator<'a>   |


----
<a name="f1">1</a>: если замыкание внутри Thread-safe


