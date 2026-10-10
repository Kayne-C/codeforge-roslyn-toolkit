# ADR 0001 — Değer eşitlikli modeller üzerinde artımlı generator'lar

**Durum:** Kabul edildi

## Bağlam

Bir source generator, IDE'de her tuş vuruşunda çalışır. Her şeyi yeniden üreten ya da hat boyunca sözdizimi düğümlerini ve sembolleri canlı tutan bir generator editörü yavaşlatır ve derleme nesnelerini sızdırır.

## Karar

- Her generator, `ForAttributeWithMetadataName` kullanan bir `IIncrementalGenerator`'dır; Roslyn, generator düğümü görmeden önce özniteliğe göre süzer.
- Hattın dönüştürme adımı sembolleri **küçük, değişmez bir modele** çevirir (record'lar ve `EquatableArray<T>`). Roslyn modelleri `Equals` ile karşılaştırır; model değişmediyse üretim adımı atlanır ve önceki çıktı yeniden kullanılır.
- Modelde derlemeye başvuran hiçbir şey saklanmaz. Tanılar düz veri olarak yakalanır (`DiagnosticInfo`: kimlik, ileti, dosya yolu, aralıklar) ve çıktı adımında `Diagnostic` nesnelerine çevrilir.
- Öznitelik bildirimleri `RegisterPostInitializationOutput` ile ve `internal` olarak üretilir; böylece CodeForge kullanan iki proje çakışmaz ve çalışma zamanında hiçbir şey gerekmez.
- Üretilen dosya adları tipin ad alanı ve adından türetilir; yeniden adlandırma eski dosyayı çift bırakmak yerine kaldırır.

## Sonuçlar

- Testler davranışı doğrudan doğrular: adım izleme açıkken özdeş derleme yalnızca `Cached`/`Unchanged` çıktı verir, ilgisiz bir düzenleme hiçbir şeyi yeniden üretmez, tek bir bildirimin değişmesi tek bir çıktıyı değiştirir.
- Modeller `ISymbol`, `SyntaxNode` ve `Location` içermemelidir. Değer eşitlikli olmayan yeni bir alan önbelleklemeyi sessizce bozardı; önbellekleme testleri bu yüzden paketin parçasıdır.
