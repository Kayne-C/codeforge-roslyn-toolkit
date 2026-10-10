# ADR 0004 — Mimari kurallar derleme düzeyi öznitelik olarak

**Durum:** Kabul edildi

## Bağlam

Katman kuralları ("domain, altyapıyı bilmemeli") genellikle geç çalışan yansıtma tabanlı testlerle ya da kimsenin denetlemediği bir gelenekle korunur. İhlalin olduğu satırda derleyicinin reddetmesini istiyoruz.

## Karar

- Kurallar yönettikleri kodun içinde bildirilir: `[assembly: ForbidDependency("Shop.Domain", "Shop.Infrastructure")]`. Proje ile birlikte sürümlenir, kod incelemesinde görünür, ek yapılandırma dosyası gerektirmez.
- Analizör öznitelikleri derleme başına bir kez okur ve yönetilen bir ad alanının içinde yazılan her tip adını denetler. Kural ad alanına ve alt ad alanlarına uygulanır, eşleşme tam bölüm bazındadır.
- **Yönetilen katmanın içindeki** başka bir tipe başvuru hiçbir zaman ihlal değildir; kural yalnızca sınırı aşınca tetiklenir.
- Bildirim bir hatadır (`CF0100`), ihlal eden adın üzerindedir ve iletide iki ad alanı ile kural yer alır.
- `using` yönergeleri tek başına bildirilmez; gerçek kullanımlar bildirilir. Böylece bir bağımlılık için iki hata çıkmaz.

## Sonuçlar

- IDE'de ve CI'da, bakımı gereken bir test projesi olmadan hızlı geri bildirim.
- Yalnızca yönetilen ad alanlarının kaynağındaki adları görür; proje başvuruları, paketler, yansıtma ve dize tabanlı erişim kapsam dışıdır. Bunlar için derlemeleri yükleyen mimari testler doğru araç olmaya devam eder.
