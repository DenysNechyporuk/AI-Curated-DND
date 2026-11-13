# AI Dungeon Master (C# + LLM)

Текстова рольова гра в стилі темного фентезі. **C#-код відповідає за стан і правила, LLM — лише за оповідь.**

## Запуск

```powershell
$env:GEMINI_API_KEY = "ваш_ключ"      # https://aistudio.google.com/apikey
dotnet run --project src/AiDnD.ConsoleApp
```

Локальна модель через Ollama (потрібна модель з підтримкою tools):

```powershell
ollama pull qwen3:8b
dotnet run --project src/AiDnD.ConsoleApp -- --provider ollama
```

Інші параметри: `--model gemini-2.5-pro`, змінна `GEMINI_MODEL`, файл `appsettings.json`
(ключ краще класти в `appsettings.local.json` — він не потрапляє в git).

Тести: `dotnet test`

## Архітектура

```
Гравець: "Я атакую гобліна мечем"
   ↓
GameMaster ── системний промпт + знімок GameState + історія + схеми інструментів ──► ILlmProvider
   ↑                                                                                   │
   │                          functionCall: attack(player_1, goblin_1, "Довгий меч")   │
   │ ◄─────────────────────────────────────────────────────────────────────────────────┘
   ├─ ToolRegistry → AttackTool → RulesEngine (d20 vs AC, урон, HP) → GameState
   ├─ результат інструмента → знову в LLM (зі свіжим станом)
   ↓
LLM пише опис → гравець бачить текст + кидки
```

| Шар | Папка | Що робить |
|---|---|---|
| Правила | `Core/Game` | `DiceRoller`, `RulesEngine`, `Character`, `GameState`, шаблони NPC |
| Інструменти | `Core/Tools` | `roll_dice`, `attack`, `skill_check`, `spawn_npc`, `give_item`, `use_item`, `move_party`, `get_character` |
| LLM | `Core/Llm` | `ILlmProvider` + `GeminiProvider` (REST) і `OllamaProvider`; фабрика за конфігом |
| Оповідач | `Core/Narration` | `GameMaster` (цикл tool calling), `CampaignMemory` (підсумки), `NpcGenerator` (JSON-схема) |
| Збереження | `Core/Persistence` | `IGameRepository` + JSON-файли (далі можна замінити на БД) |
| UI | `ConsoleApp` | консоль; ядро не залежить від UI, тож пізніше можна додати WPF/Avalonia/веб |

### Захист від «чітерства» ШІ
- Модель не може змінити HP чи кинути кубик — лише попросити інструмент.
- Помилкові виклики (неіснуючий id, чужа зброя) повертаються моделі як `{"error": ...}`.
- Згенеровані NPC проходять валідацію (HP ≤ 60, AC ≤ 20, характеристики 3..18).
- Ліміт раундів інструментів за хід (захист від зациклення).

## Команди в грі
`/help`, `/state`, `/sheet [id]`, `/log`, `/memory`, `/npc <опис>`, `/roll 2d6+1`, `/save`, `/load`, `/quit`

## Що далі
- UI (Avalonia/WPF або Blazor) поверх `GameMaster` — подія `ToolExecuted` вже є для анімації кубиків
- Бій з ініціативою та чергою ходів
- RAG-пам'ять (embeddings NPC/локацій + векторний пошук)
- БД замість JSON (SQLite / SQL Server через EF Core)
- Мультиплеєр (SignalR)
