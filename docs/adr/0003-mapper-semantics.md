# ADR 0003 — Mapper anlambilimi: açık, derleme zamanlı, tahmin yok

**Durum:** Kabul edildi

## Karar

Eşleme, kaynak tip üzerindeki `[MapTo(typeof(THedef))]` ile `To{Hedef}()` uzantı metodu olarak üretilir.

- **Üyeler ada göre, büyük/küçük harfe duyarsız eşleşir.** Kaynak üyedeki `[MapProperty("Diger")]` eşleşmeyi yeniden adlandırır; `[MapIgnore]` üyeyi çıkarır.
- **Oluşturma:** en çok kaynak üyesiyle karşılanabilen (ve eşleşmemiş zorunlu parametre gerektirmeyen) yapıcı seçilir; kalan yazılabilir üyeler nesne initializer'ı ile atanır.
- **Dönüşümler kapalı bir listedir:** özdeş tipler; örtük sayısal genişletme; `T` ↔ `T?`; üyeleri aynı olan enum'lar; o hedef için kendi `[MapTo]`'su olan tipler; ve bunların `List<T>`, dizi, `IEnumerable<T>`, `IReadOnlyList<T>`, `IList<T>`, `ICollection<T>` biçimleri. Bunun dışındaki her şey **CFG102, bir hatadır**.
- **Eksik kaynak sessizlik değil uyarıdır.** Hedef üye varsayılanını korur (`CFG101`); `[MapIgnore]` niyeti açık eder. Kaynağı olmayan `required` üye veya yapıcı parametresi üretimi durdurur, çünkü sonuç derlenmez ya da yanlış olur.
- **Null durumu bildirilir** (`CFG103`): null olabilen kaynak, null olamayan hedefi beslerse.
- Null kaynak, yarım kurulmuş nesne üretmek yerine `ArgumentNullException` fırlatır.

## Sonuçlar

- Davranış öngörülebilir ve gözden geçirilebilirdir: üretilen metot düz koddur.
- Özellik kümesi genel amaçlı eşleme kütüphanelerinden bilerek daha küçüktür (düzleştirme yok, özel dönüştürücü yok, çok biçimlilik yok); bkz. README sınırları.
