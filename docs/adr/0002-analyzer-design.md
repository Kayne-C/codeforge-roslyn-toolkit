# ADR 0002 — Analizör tasarımı

**Durum:** Kabul edildi

- **Anlamın önemli olduğu yerde sözdizimi yerine operasyonlar.** `.Result`, `Wait()`, `Thread.Sleep`, `DateTime.UtcNow` ve `new HttpClient()` `IOperation` eylemleriyle bulunur; kural yazılışa değil çözümlenmiş üyeye bağlıdır. İlgisiz bir tipteki `Result` adlı özellik hiçbir zaman bildirilmez.
- **Yapının önemli olduğu yerde sözdizimi.** Mimari kural adlarla (`IdentifierName`, `GenericName`, `QualifiedName`) çalışır ve her birini anlamsal modelle çözer, çünkü bildirim kaynakta tam başvurunun üzerine düşmelidir.
- **Bağlam, kapsayan sembolden değil sözdiziminden.** Bir çağrının `async` metotta olup olmadığı sözdizimi atalarına (metot, yerel fonksiyon, lambda) bakılarak belirlenir. Operasyonun kapsayan sembolü lambda değil üyedir; bu, bir test yakalayana kadar `Thread.Sleep`'in async lambdalarda gizli kalmasına yol açmıştı.
- **Yerleşik kurallarla çakışma yok.** `CancellationToken` iletme `CA2016` ile kapsandığından tekrarlanmaz.
- **Önem düzeyi kullanıcının seçimi.** Tanımlayıcıların makul varsayılanları vardır; `.editorconfig` bunları değiştirir.
- **Code fix yalnızca güvenli olduğunda.** `.Result` / `Wait()` / `GetAwaiter().GetResult()` yalnızca `async` bağlamda düzeltilir ve parantez yalnızca çevre ifade gerektirdiğinde eklenir. Bir test, async olmayan metotta düzeltme sunulmadığını doğrular.
- Analizörler üretilmiş kodu atlar ve eşzamanlı çalışır.
