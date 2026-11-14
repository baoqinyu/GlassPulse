# GlassPulse 1.2.1

- Add Claude Fable 5.2 and Mythos 5.2 standard API-equivalent pricing: $8 input, $40 output, $0.20 cache reads, $10 five-minute cache writes and $16 one-hour cache writes per 1M tokens.
- Refresh the bundled catalog to 32 models on 2026-11-13 and record the official model and pricing sources. Existing installations add these missing models on startup while preserving edited rates.
- Keep separate prices for Fable / Mythos 5 and 5.1; version 5.2 uses standard rates through its full 1M context window.
- Add seven regression scenarios covering all token categories, aliases, unknown versions, long context, historical prices and Claude log-to-weekly-cost accounting.

Validation: 64 accounting/configuration scenarios and 18 native-window cases.

---

- ���� Claude Fable 5.2 �� Mythos 5.2 ��׼ API ��ֵ�۸�ÿ���� token������ $8����� $40�������ȡ $0.20��5 ���ӻ���д�� $10��1 Сʱ����д�� $16��
- ���ü۸���� 2026-11-13 ������ 32 ��ģ�ͣ������ٷ���Դ�����а�װ����ʱ�Ჹ��ȱʧģ�Ͳ������ѱ༭�ĵ��ۡ�
- 5 �� 5.1 �汾�ֱ�Ƽۣ�5.2 ������ 1M ��������ʹ����ͬ���ۡ�
- �����߸��ع鳡�������Ǹ� token ���ģ�ͱ�����δ֪�汾���������ġ���ʷ�۸��� Claude ��־�ܷ��û��ܡ�

��֤��64 ������ / ���ó����� 18 ��ԭ�����ڼ�顣

---

# GlassPulse 1.2.0

- Restore compact start�Cend week labels while retaining historical selection.
- Read plain and `.jsonl.zst` Codex history with streaming, checksum-verified embedded Zstandard support; no Python or ccusage runtime dependency.
- Follow canonical Codex token-count deltas, unchanged-snapshot suppression, cross-file/fork replay deduplication, and explicitly uncovered compaction requests. Ordinary request metadata is not billed twice.
- Respect plain/active rollout precedence and label reference-model alias estimates.
- Keep existing visual pixel-clipping, themes, provider choices and budgets unchanged.

Validation: 57 accounting/configuration cases, 18 native-window cases, synthetic UI checks, and a private ccusage 20.0.40 comparison over four complete September weeks. Token categories match exactly; displayed dollar totals agree. No real usage, session identifiers or workstation paths are included in public fixtures.

---

- ��ѡ��ָ�Ϊ��ʼ�C�������ڷ�Χ��
- ֧����ͨ�� `.jsonl.zst` ѹ����ʷ����ǶУ�����ʽ�����������в����� Python / ccusage��
- ��������Ԫ�����ظ��������ۼƿ��ա����ļ� / ���Ự���Ƽ���������������Ĵ�����
- ��ͨ / ��������ȣ��ο�ģ�ͱ����Ƽ���ȷ��עΪ���㡣
- ��������Բ�����زü������⡢����ѡ���Ԥ�㹦�ܡ�

��֤��57 ������ / ���ó�����18 ��ԭ�����ڼ�顢��ʾ�����飬�Լ��� ccusage 20.0.40 ���ĸ�����������˽�ܶ��ա����� token ��ȷһ�£���ʾ���һ�¡���������������ʵ�������Ự��ʶ�򱾻�·����
