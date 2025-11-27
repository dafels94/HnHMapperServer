# Haven & Hearth Cookbook Algorithm Documentation

**Version:** 2.1
**Last Updated:** 2025-11-25
**Status:** Production-Ready

---

## Table of Contents

1. [Overview](#overview)
2. [Core Concepts](#core-concepts)
3. [The Algorithm](#the-algorithm)
4. [Configuration Parameters](#configuration-parameters)
5. [Implementation Details](#implementation-details)
6. [Usage Examples](#usage-examples)
7. [Research & Design Rationale](#research--design-rationale)
8. [Edge Cases & Limitations](#edge-cases--limitations)
9. [Performance Considerations](#performance-considerations)
10. [Future Enhancements](#future-enhancements)

---

## Overview

### Purpose

The **Next-Generation Cookbook Algorithm** solves a critical problem in Haven & Hearth food analysis: identifying the truly best foods among 38,000+ recipes while accounting for:

- **Tier bonuses** (+2 tier FEPs grant 2× stat gains when consumed)
- **Stat concentration** (focused foods reduce RNG variance)
- **Expected value** (probability-weighted stat gains per consumption)
- **Efficiency** (expected stats per hunger point with concentration bonus)

### Key Features

✅ **Tier-Adjusted Expected Value** - Accounts for 2× stat gain on +2 tier FEPs
✅ **Concentration Bonus** - Rewards foods with focused stat distribution
✅ **Stat Grouping** - AGI +1 and AGI +2 treated as same stat for concentration
✅ **RNG-Aware** - Higher concentration = higher chance to hit desired stat
✅ **Purity Tracking** - Measures % of useful FEPs (excludes CHA trash)

### Design Goal

> **Find foods with high expected stat gains, focused composition, and low hunger cost**

---

## Core Concepts

### 1. Attribute Categorization

All 9 Haven & Hearth attributes are categorized by role effectiveness:

| Category | Attributes | Purpose | Weight |
|----------|-----------|---------|--------|
| **Pure Fighter** | STR, AGI, CON | Combat effectiveness | 1.0× |
| **Pure Crafter** | PSY, DEX, WILL | Crafting efficiency | 1.0× |
| **Universal** | INT, PER | Useful for both roles | 0.7× to each |
| **Trash** | CHA | Dilutes food quality | **Ignored!** |

**Why CHA is trash:**
- CHA has minimal gameplay impact
- High CHA foods have lower useful FEP density
- Same food name can have 15%-85% purity depending on CHA content
- Example: Two "Spicy Salad" variants:
  - Variant A: 68% CHA (BAD) → Low purity, low scores
  - Variant B: 15% CHA (GOOD) → High purity, high scores

### 2. FEP Tier System & RNG Mechanics

FEPs come in different tiers (e.g., "STR +1" vs "STR +2"):

- **Tier +1**: Grants 1 stat point when consumed
- **Tier +2**: Grants **2 stat points** when consumed (2× bonus!)

**How Eating Works:**
1. Game picks which FEP fires based on FEP bar composition (RNG)
2. If it picks "STR +1" (e.g., 60% of bar) → you get **1 STR**
3. If it picks "STR +2" (e.g., 10% of bar) → you get **2 STR**

**Example Food:**
- AGI +1: 1.0 FEP (58.8% chance)
- AGI +2: 0.2 FEP (11.8% chance)
- STR: 0.5 FEP (29.4% chance)

**Expected value per consumption:**
```
= (1.0/1.7) × 1 AGI + (0.2/1.7) × 2 AGI + (0.5/1.7) × 1 STR
= 0.588 + 0.235 + 0.294
= 1.117 total stats
```

### 3. Key Metrics

#### Purity
```
Purity = (Total FEPs - CHA FEPs) / Total FEPs
```
- Range: 0.0 (100% CHA) to 1.0 (0% CHA)
- >90% purity = "High Purity" bonus
- Shows as percentage in UI (e.g., 85%)

#### Expected Stat Value
```
For each FEP:
  probability = FEP value / total FEP
  tierMultiplier = 2 if "+2" tier, else 1
  expectedValue += probability × tierMultiplier

ExpectedStatValue = Σ(probability × tierMultiplier)
```

**Example:**
- AGI +1: 1.0 FEP → (1.0/1.7) × 1 = 0.588
- AGI +2: 0.2 FEP → (0.2/1.7) × 2 = 0.235
- STR: 0.5 FEP → (0.5/1.7) × 1 = 0.294
- **Total: 1.117 stats expected**

#### Concentration Bonus
```
1. Group FEPs by base stat (AGI +1 and AGI +2 → "AGI")
2. Find dominant stat (highest combined FEP)
3. ConcentrationBonus = 1 + (DominantStatPercent / 100)
```

**Example:**
- AGI total: 1.2 FEP (70.6%) ← Dominant
- STR total: 0.5 FEP (29.4%)
- **Bonus: 1 + 0.706 = 1.706**

**Why:** Higher concentration = higher chance RNG hits desired stat

#### Efficiency (Final Metric)
```
Efficiency = (ExpectedStatValue / Hunger) × ConcentrationBonus
```

**Example:**
- Expected: 1.117 stats
- Hunger: 10
- Dominant: 70.6% AGI
- **Efficiency = (1.117 / 10) × 1.706 = 0.191**

**Why this matters:** Accounts for both stat gain potential AND RNG reliability

---

## The Algorithm

### Step 1: Group FEPs by Base Stat

For each food, group FEPs by removing tier suffix:

```csharp
// Helper: Extract base stat name (remove "+1", "+2")
string GetBaseStat(string attributeName) {
    return attributeName.Split('+')[0].Trim();
}

// Group FEPs by base stat
var groupedByBaseStat = food.Feps
    .GroupBy(f => GetBaseStat(f.AttributeName))
    .Select(g => new {
        BaseStat = g.Key,
        TotalFep = g.Sum(f => f.BaseValue)
    })
    .OrderByDescending(g => g.TotalFep)
    .ToList();

// Find dominant stat
var dominantStat = groupedByBaseStat.First();
var totalFep = food.Feps.Sum(f => f.BaseValue);
var dominantStatPercent = (dominantStat.TotalFep / totalFep) * 100;
```

**Example:**
- AGI +1: 1.0, AGI +2: 0.2 → AGI total: 1.2 (70.6%)
- STR: 0.5 → STR total: 0.5 (29.4%)
- **Dominant: AGI at 70.6%**

### Step 2: Calculate Tier-Adjusted Expected Stat Value

For each FEP, calculate probability-weighted expected value:

```csharp
// Helper: Get tier multiplier
int GetTierMultiplier(string attributeName) {
    if (attributeName.Contains("+2")) return 2;
    return 1;  // +1 or no tier
}

// Calculate expected stat value
double expectedStatValue = 0;
foreach (var fep in food.Feps)
{
    var probability = (double)fep.BaseValue / totalFep;
    var statGain = GetTierMultiplier(fep.AttributeName);
    expectedStatValue += probability * statGain;
}
```

**Example:**
- AGI +1: (1.0/1.7) × 1 = 0.588
- AGI +2: (0.2/1.7) × 2 = 0.235
- STR: (0.5/1.7) × 1 = 0.294
- **Expected: 1.117 stats**

### Step 3: Apply Concentration Bonus & Calculate Efficiency

```csharp
// Calculate concentration bonus
var baseEfficiency = expectedStatValue / food.Hunger;
var concentrationBonus = 1 + (dominantStatPercent / 100);
var efficiency = baseEfficiency * concentrationBonus;
```

**Example:**
- Base: 1.117 / 10 = 0.112
- Bonus: 1 + 0.706 = 1.706
- **Final: 0.112 × 1.706 = 0.191**

### Step 4: Assign Dynamic Tiers (Percentile-Based)

**CRITICAL:** This MUST be done on the FULL filtered dataset, not a paginated subset!

```csharp
// Sort ALL foods by efficiency
var sorted = foods.OrderByDescending(f => f.Efficiency).ToList();

// Calculate percentile cutoffs
var bestIdx = (int)(foods.Count * 0.20) - 1;  // Top 20%
var midIdx = (int)(foods.Count * 0.60) - 1;   // Top 60%

var bestCutoff = sorted[bestIdx].Efficiency;
var midCutoff = sorted[midIdx].Efficiency;

// Assign base tiers
foreach (var food in foods) {
    if (food.Efficiency >= bestCutoff)
        food.TierValue = 2.0;
    else if (food.Efficiency >= midCutoff)
        food.TierValue = 1.0;
    else
        food.TierValue = 0.0;
}
```

### Step 5: Apply Tier Modifiers

```csharp
foreach (var food in foods) {
    decimal adjustment = 0;

    // Boost: 2+ tier +2 FEPs (+1.0)
    if (food.Tier2Count >= 2)
        adjustment += 1.0;

    // Boost: Has PSY (rare stat) (+0.5)
    if (food.HasPsy)
        adjustment += 0.5;

    // Boost: High purity (>90%) (+0.5)
    if (food.Purity >= 0.90)
        adjustment += 0.5;

    // Penalty: Very high hunger (>15) (-1.0)
    if (food.Hunger > 15)
        adjustment -= 1.0;

    // Apply and clamp
    food.TierValue += adjustment;
    food.TierValue = Math.Max(0.0, Math.Min(2.0, food.TierValue));

    // Map to tier name
    food.Tier = food.TierValue switch {
        >= 1.75 => "Best",
        >= 0.75 => "Mid",
        _       => "Low"
    };
}
```

---

## Configuration Parameters

**Location:** `Cookbook.razor` (efficiency calculation inline)

| Parameter | Value | Purpose |
|-----------|-------|---------|
| `Tier2Multiplier` | 2.0 | Stat multiplier for tier +2 FEPs (2× stats when eaten) |
| `Tier1Multiplier` | 1.0 | Stat multiplier for tier +1 FEPs (1× stats when eaten) |
| `ConcentrationFormula` | `1 + (DominantPercent / 100)` | Rewards focused stat distribution |

**Tuning Notes:**
- **2.0× tier +2 multiplier**: Reflects actual game mechanic - tier +2 FEPs grant 2× stats
- **Concentration bonus**: Linear scaling rewards focused foods (50% concentration = 1.5× multiplier, 80% = 1.8×)
- **Stat grouping**: AGI +1 and AGI +2 grouped as "AGI" prevents dilution from multiple tiers

---

## Implementation Details

### File Locations

**Core Efficiency Algorithm:**
- `src/HnHMapperServer.Web/Helpers/FoodHelpers.cs`
  - `CalculateTierAdjustedEfficiency(FoodDto)` - Main efficiency calculation
  - `GetBaseStat()` - Extracts base stat name (e.g., "AGI +2" → "AGI")
  - `GetTierMultiplier()` - Returns 2 for "+2", 1 otherwise
  - `GetDominantStat()` - Finds dominant stat and percentage
  - `GetFepColor()`, `GetFepColorStyle()` - Stat color coding

**Food Service:**
- `src/HnHMapperServer.Services/Services/FoodService.cs`
  - `SearchFoodsAsync()` - Main search with `Hunger > 0` filter
  - `GetAllFoodsAsync()` - Cached food loading with filter
  - Uses `EF.Functions.Like()` for SQLite-compatible search

**Metrics Calculation:**
- `src/HnHMapperServer.Services/Services/FoodMetricsCalculator.cs`
  - Purity calculation (excludes CHA)
  - Role scores (Fighter, Crafter)
  - Tier assignment

**DTOs:**
- `src/HnHMapperServer.Core/DTOs/FoodDto.cs`
  - `Purity`, `FighterPercent`, `CrafterPercent` properties
  - `Efficiency` property (calculated server-side)
  - `FoodSearchDto` - Search/filter parameters
  - `GroupedFoodDto` - For grouping food variants

**UI Components:**
- `src/HnHMapperServer.Web/Components/Pages/Cookbook.razor`
  - Debounced auto-search (300ms)
  - Client-side group pagination
  - Race condition handling with search ID tracking
- `src/HnHMapperServer.Web/Components/Shared/FoodGroupPanel.razor`
  - Food group display with expansion
- `src/HnHMapperServer.Web/Components/Shared/FoodEfficiencyCell.razor`
  - Efficiency display with dominant stat indicator
- `src/HnHMapperServer.Web/Components/Shared/FoodFepsBar.razor`
  - Color-coded FEP composition bars
- `src/HnHMapperServer.Web/Components/Pages/RecipeDetails.razor`
  - Food details page

### Database Schema

**Relevant Tables:**
- `Foods` - Core food data (Id, Name, Hunger, Energy, etc.)
- `FoodFeps` - FEP entries (FoodId, AttributeName, BaseValue)
- `FoodIngredients` - Recipe ingredients (FoodId, Name, Quantity, Quality)

**Note:** Tiers are calculated on-the-fly, NOT stored in database. This ensures tier assignments always reflect the current dataset state.

### Dependency Injection

**Registration:** `src/HnHMapperServer.Api/Program.cs:130`
```csharp
builder.Services.AddScoped<FoodMetricsCalculator>();
```

**Dependencies:**
- `ILogger<FoodMetricsCalculator>` - For FEP format warnings

---

## Usage Examples

### Example 1: Find Best Fighter Foods

**Search Criteria:**
- Filter by FEP type: "STR" (ensures fighter-focused foods)
- Sort by: "Efficiency"
- Tier: "Best"
- Max Hunger: 5 (avoid heavy meals)

**Expected Results:**
- High STR/AGI/CON FEPs
- Minimal CHA contamination
- Low hunger cost
- Top 20% efficiency globally

**Sample Result:**
```
Name: "Spicy Salad"
Tier: Best (🥇)
Purity: 85%
Efficiency: 8.5
Fighter Score: 12.3
Crafter Score: 3.1
Fighter Percent: 80%
Hunger: 1
FEPs: STR +2: 1.9, AGI +1: 1.6, CON +1: 1.9
```

### Example 2: Find High-Purity Foods

**Search Criteria:**
- Min Purity: 95%
- Sort by: "Purity"

**Expected Results:**
- <5% CHA contamination
- Clean stat distribution
- May have lower total FEPs but better quality

**Why This Matters:** Two "Spicy Salad" variants might have identical names but vastly different purity:
- Variant A: 15% purity (68% CHA trash) → Efficiency: 2.1 → Low tier
- Variant B: 85% purity (15% CHA trash) → Efficiency: 8.5 → Best tier

### Example 3: Find Rare PSY Foods

**Search Criteria:**
- Filter by FEP type: "PSY"
- Sort by: "PowerScore"
- Tier: "Best" or "Mid"

**Expected Results:**
- Only ~6.9% of foods have PSY
- Automatically get +0.5 tier bonus
- Excellent for crafter builds

### Example 4: Avoid CHA-Contaminated Variants

**Search Criteria:**
- Search Term: "Spicy Salad"
- Min Purity: 70%
- Sort by: "Efficiency"

**Result:** Filters out the bad "Spicy Salad" variants with high CHA content.

---

## Research & Design Rationale

### Why CHA is "Trash"

**Empirical Analysis:**
1. **Database Analysis (~38,317 valid foods after filtering Hunger=0):**
   - CON: ~24,000 foods (63% have CON)
   - STR: ~20,000 foods (52%)
   - PSY: ~2,600 foods (7% - rarest)
   - CHA: ~21,500 foods (56%)

2. **Gameplay Impact:**
   - CHA has minimal effect on combat or crafting
   - Other stats directly improve core gameplay loops
   - High CHA reduces density of useful stats

3. **User Feedback:**
   > "CHA should be eliminated from equation, since most of this just messes up food, the less cha the better"

4. **Food Variant Quality:**
   - Same food name can have 15%-85% purity
   - High-purity variants are objectively better
   - Purity metric identifies good variants

### Why 2.0× Multiplier for Tier +2

**Game Mechanic:** When you eat a food and RNG hits a +2 tier FEP, you gain **2× stat points**.

**Example:**
- Food has "STR +2: 0.5" FEP (10% of bar)
- If RNG hits it → you get **2 STR** (not 1 STR)

**Why We Use Expected Value:**
- **Wrong approach:** Multiply FEP value by 1.5 (arbitrary)
- **Right approach:** Calculate probability × actual stat gain
- Formula: `(0.5/5.0) × 2 = 0.2` expected STR per consumption

**Impact:**
- Tier +2 FEPs ARE worth 2× when they fire
- But they're often small portions of the bar (low probability)
- Expected value formula captures this correctly

### Why Concentration Bonus

**Problem:** Two foods with same expected value can have different RNG reliability.

**Example:**
- **Food A:** 100% AGI (1.0 FEP) → 100% chance to get AGI
- **Food B:** 50% AGI, 50% STR → 50% chance to get AGI

If you want AGI, Food A is strictly better!

**Solution: Concentration Bonus**
```
ConcentrationBonus = 1 + (DominantStatPercent / 100)
```

**Examples:**
- 100% concentration → 2.0× multiplier (perfect targeting)
- 70% concentration → 1.7× multiplier (good targeting)
- 50% concentration → 1.5× multiplier (split evenly)

**Why Linear Scaling:**
- Simple, intuitive formula
- Directly rewards focus
- Prevents over-rewarding extreme cases

### Why Group +1 and +2 Tiers Together

**Problem:** Food with "AGI +1: 1.0" and "AGI +2: 0.2" looks scattered.

**Wrong Approach:**
- Treat as separate stats
- Dominant = AGI +1 (58.8%)
- Concentration bonus = 1.588

**Right Approach:**
- Group as "AGI"
- Total AGI = 1.2 FEP (70.6%)
- Concentration bonus = 1.706 ✅

**Rationale:**
- Both give AGI stats (just different amounts)
- Player targeting AGI wants either to fire
- Grouping reflects actual RNG targeting strategy

### Why Dynamic Percentile Tiers

**Alternative:** Fixed tier thresholds (e.g., "Efficiency >10 = Best")

**Problem:**
- As players discover new foods, thresholds shift
- Today's "Best" might be tomorrow's "Mid"
- Fixed thresholds become stale

**Solution:** Percentile-based tiers
- Top 20% always "Best" (adapts to dataset)
- Top 60% always "Mid"
- Bottom 40% always "Low"
- System automatically adjusts as new foods discovered

**Trade-off:**
- Tiers are relative, not absolute
- A food's tier can change as dataset grows
- But this reflects actual gameplay (better foods get discovered)

### Why PSY Bonus

**Data:** PSY appears in only 3,380 / 49,327 foods (6.9%)

**Rationale:**
- PSY is the rarest stat
- Crafter builds desperately need PSY
- +0.5 tier bonus rewards rarity
- Helps PSY foods compete with more common foods

### Why Purity Bonus

**Observation:** Purity >90% is rare and valuable.

**Rationale:**
- >90% purity means <10% CHA contamination
- These foods are objectively higher quality
- +0.5 tier bonus rewards clean stat distribution
- Encourages players to seek pure food variants

---

## Edge Cases & Limitations

### 1. Single Food Tier Display

**Issue:** When viewing a single food (recipe details page), tier shows as "Unknown".

**Reason:** Tier is relative to the full dataset. Calling `AssignTiers()` on a 1-food list would always return "Best" tier (misleading).

**Solution:** Tier is set to "Unknown" for single food views. All other metrics (purity, efficiency, role scores) are still calculated and displayed.

**Alternative:** Could load all foods in background to calculate true tier, but this is expensive for a single page view.

### 2. Negative FEP Values (Debuffs)

**Behavior:** Negative FEPs reduce scores (as expected).

**Example:**
- Food has "STR +2: -3.0" (debuff)
- Weighted value: -3.0 × 1.5 = -4.5
- Reduces FighterScore by 4.5

**Rationale:** Tier +2 debuffs ARE worse than tier +1 debuffs. The 1.5× multiplier is mathematically consistent.

**Frequency:** Rare in the dataset (most foods have only positive FEPs).

### 3. Zero Hunger Foods

**Handling:** Foods with `Hunger = 0` are **filtered out** at the database query level.

```csharp
// In FoodService.cs
var query = _context.Foods
    .Where(f => f.Hunger > 0)  // Filter out incomplete data
    ...
```

**Rationale:**
- 11,010 foods (22%) had `Hunger = 0` - incomplete/bad data entries
- Same food names often have both good entries (with hunger) and bad entries (without)
- Efficiency cannot be calculated when Hunger = 0 (division by zero)
- Filtering reduces dataset from 49,327 to ~38,317 valid foods
- UI previously showed "N/A" for these foods - now they're simply excluded

### 4. All-CHA Foods

**Behavior:**
- Purity = 0%
- FighterScore = 0
- CrafterScore = 0
- PowerScore = 0
- Efficiency = 0
- Tier = Low

**Frequency:** Extremely rare. Most foods have at least one useful FEP.

### 5. Unrecognized FEP Attributes

**Handling:**
- `ExtractAttribute()` returns empty string
- Empty string doesn't match any category
- FEP is silently ignored in scoring
- **Warning logged:** `"Unrecognized FEP attribute: 'XYZ' from 'XYZ +2'"`

**Common Causes:**
- Malformed FEP names in database
- New attributes added to game (not yet in our categorization)
- Case sensitivity issues (now fixed with case-insensitive regex)

### 6. Foods with No FEPs

**Behavior:**
- TotalFep = 0
- UsefulFep = 0
- Purity = 1.0 (100% - technically no CHA!)
- All scores = 0
- Efficiency = 0 (or 0 × 1000 = 0 if hunger = 0)
- Tier = Low

**Frequency:** Should be rare (why would a food have no FEPs?).

---

## Performance Considerations

### Current Implementation

**SearchFoodsAsync behavior:**
1. Filters foods with `Hunger > 0` (excludes 11K incomplete entries)
2. Loads foods matching base filters (text search, ingredient, FEP type, max hunger)
3. Converts to DTOs
4. Calculates metrics for all foods
5. Assigns tiers based on filtered dataset
6. Applies advanced filters (purity, tier, efficiency)
7. Applies sorting
8. Returns up to 2000 foods (client-side pagination by groups)

**Client-Side Pagination:**
- API returns up to 2000 foods matching filters
- Client groups foods by name (e.g., all "Meatpie" variants together)
- Pagination operates on **groups**, not individual foods
- Each page shows 20 groups with all their variants

**Memory Usage (Typical):**
- ~38,317 valid foods × ~500 bytes each = **~19 MB total**
- Search results limited to 2000 foods = ~1 MB per search
- Plus FEP/Ingredient data: ~2-5 MB per search

**Latency:**
- Loading filtered foods from SQLite: ~100-200ms
- DTO conversion: ~30-50ms
- Metric calculation: ~100-200ms
- **Total: ~300-500ms per search**

**UI Responsiveness:**
- Debounced auto-search (300ms delay)
- Race condition handling (stale results ignored)
- No search button required - results update as you type

### Optimization Opportunities

#### Option 1: Database-Stored Tiers (Recommended)

**Changes:**
1. Add `TierValue REAL` column to `Foods` table
2. Background job recalculates tiers every 5-10 minutes
3. Queries use pre-calculated tiers (fast!)

**Benefits:**
- Query time: ~10-50ms (1000× faster!)
- Memory usage: Only load requested page (20 foods × 500 bytes = 10 KB)
- Advanced filters work on database side (efficient)

**Trade-offs:**
- Tiers are slightly stale (up to 10 minutes old)
- Need background job infrastructure
- Schema migration required

**Implementation:**
```csharp
// Background job (every 10 minutes)
public class TierRecalculationService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            // Load ALL foods
            var foods = await _context.Foods.Include(f => f.Feps).ToListAsync();
            var foodDtos = foods.Select(MapToDto).ToList();

            // Calculate metrics and tiers
            foreach (var food in foodDtos)
                _calculator.CalculateFoodMetrics(food);
            _calculator.AssignTiers(foodDtos);

            // Save tiers to database
            foreach (var (dto, entity) in foodDtos.Zip(foods))
                entity.TierValue = dto.TierValue;
            await _context.SaveChangesAsync();

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
}
```

#### Option 2: Caching with Redis/MemoryCache

**Changes:**
1. Cache tier calculations for 5-10 minutes
2. Cache key: hash of base filters
3. First search is slow, subsequent searches are fast

**Benefits:**
- No schema changes
- Fast for repeated searches
- Easy to implement

**Trade-offs:**
- Cache invalidation complexity
- Cache misses still slow
- Memory usage on cache server

#### Option 3: Lazy Tier Calculation

**Changes:**
1. Don't calculate tiers by default
2. Only calculate if user filters by tier
3. Show "efficiency" instead of "tier" as default sort

**Benefits:**
- Faster default searches
- Only pay cost when needed

**Trade-offs:**
- Inconsistent UX (tier sometimes available, sometimes not)
- Defeats purpose of tier system

### Current Performance (Acceptable)

With ~38,317 valid foods, current implementation is acceptable for:
- Small deployments (single tenant, <10 concurrent users)
- Testing and development
- Datasets <100K foods

Monitor query times and upgrade to Option 1 (DB-stored tiers) if:
- Search latency >2 seconds
- Multiple concurrent users
- Dataset grows >100K foods

---

## Future Enhancements

### 1. Quality-Adjusted Scoring

**Problem:** Current algorithm uses base FEP values, ignores ingredient quality.

**Enhancement:**
```csharp
EffectiveFEP = BaseFEP × sqrt(Quality / 10)
```

**Example:**
- STR +2: 2.0 base
- At Q10: 2.0 × sqrt(10/10) = 2.0 × 1.0 = **2.0**
- At Q40: 2.0 × sqrt(40/10) = 2.0 × 2.0 = **4.0**
- At Q100: 2.0 × sqrt(100/10) = 2.0 × 3.16 = **6.32**

**Impact:** Tier assignments would account for ingredient availability and achievable quality.

**Implementation:** Already partially supported via `/api/foods/calculate-feps` endpoint (used in recipe details page).

### 2. Meal Planning Optimization

**Goal:** Find optimal combination of foods for a specific build.

**Algorithm:**
- Input: Desired stat targets (e.g., 50 STR, 30 INT, 20 PSY)
- Output: Minimal hunger meal plan achieving targets
- Constraint: Maximize purity, minimize CHA

**Approach:** Linear programming or genetic algorithm.

### 3. Ingredient Substitution Suggestions

**Goal:** Suggest ingredient swaps to improve food purity.

**Example:**
- "Spicy Salad" variant A: Uses "Lettuce" (high CHA)
- "Spicy Salad" variant B: Uses "Cabbage" (low CHA)
- Suggestion: "Replace Lettuce with Cabbage to increase purity from 32% to 85%"

### 4. Historical Tier Tracking

**Goal:** Track how food tiers change over time as dataset grows.

**Implementation:**
- Store tier snapshots weekly
- Show tier history graph on recipe details page
- Identify foods whose value is increasing/decreasing

### 5. Custom Attribute Weights

**Goal:** Let users customize which stats they care about.

**UI:**
```
Fighter Weight: [slider: 0-100%]
Crafter Weight: [slider: 0-100%]

Custom Weights:
- STR: [slider: 0-200%]
- AGI: [slider: 0-200%]
- ...
```

**Impact:** Pure STR builds could set `STR weight = 200%`, ignore other stats.

### 6. Machine Learning Tier Prediction

**Goal:** Predict food tier from ingredient list (before cooking).

**Training Data:** 49,327 foods with ingredients → tier assignments

**Model:** Random forest or gradient boosting

**Use Case:** "Should I cook this recipe?" (before spending resources)

### 7. Bulk Food Import/Export

**Goal:** Share food databases between tenants.

**Format:** JSON export of all foods with FEPs

**Use Case:** Import community-curated food database

### 8. FEP Heatmap Visualization

**Goal:** Visual representation of stat distribution.

**Example:**
```
Food: "Spicy Salad"

STR ████████ 8.0
AGI ██████ 6.0
CON ████ 4.0
INT ██ 2.0
CHA █ 1.0 (trash!)
```

---

## Appendix A: Quick Reference

### Formulas

| Metric | Formula |
|--------|---------|
| Purity | `(TotalFEP - CHA) / TotalFEP` |
| Fighter Score | `(STR + AGI + CON) + (INT + PER) × 0.7` |
| Crafter Score | `(PSY + DEX + WILL) + (INT + PER) × 0.7` |
| Power Score | `FighterScore × 0.5 + CrafterScore × 0.5` |
| Efficiency | `PowerScore / Hunger` |
| Tier Adjustments | `+1.0 (2+ tier +2), +0.5 (PSY), +0.5 (>90% purity), -1.0 (hunger >15)` |

### Tier Thresholds

| Tier | TierValue Range | Percentile | Color | Icon |
|------|----------------|------------|-------|------|
| **Best** | ≥1.75 | Top 20% | Green | 🥇 |
| **Mid** | 0.75 - 1.74 | 20% - 60% | Yellow | 🥈 |
| **Low** | 0.0 - 0.74 | Bottom 40% | Gray | 🥉 |

### Attribute Categories

| Fighter | Crafter | Universal | Trash |
|---------|---------|-----------|-------|
| STR, AGI, CON | PSY, DEX, WILL | INT, PER | CHA |

---

## Appendix B: Sample Output

### Example Food: "Spicy Salad +2" (High Quality Variant)

```json
{
  "id": 12345,
  "name": "Spicy Salad",
  "hunger": 1,
  "energy": 500,
  "feps": [
    { "attributeName": "STR +2", "baseValue": 1.9 },
    { "attributeName": "AGI +1", "baseValue": 1.6 },
    { "attributeName": "CON +1", "baseValue": 1.9 },
    { "attributeName": "DEX +1", "baseValue": 0.7 },
    { "attributeName": "CHA +1", "baseValue": 1.0 }
  ],

  // Calculated Metrics
  "totalFep": 6.1,
  "chaTrash": 1.0,
  "usefulFep": 5.1,
  "purity": 0.836,  // 83.6%

  "fighterScore": 6.34,  // (1.9×1.5 + 1.6 + 1.9) = 6.35
  "crafterScore": 0.70,  // (0.7) = 0.7
  "fighterPercent": 90,
  "crafterPercent": 10,

  "powerScore": 3.52,  // (6.34×0.5 + 0.7×0.5)
  "efficiency": 3.52,  // 3.52 / 1

  "tier2Count": 1,
  "hasPsy": false,
  "tierValue": 1.5,  // Base 1.0 (mid-percentile) + 1.0 (tier +2 bonus) - 0.5 (purity <90%)
  "tier": "Mid"
}
```

### Example Food: "Grilled Steak" (Fighter Food)

```json
{
  "id": 67890,
  "name": "Grilled Steak",
  "hunger": 3,
  "energy": 1200,
  "feps": [
    { "attributeName": "STR +2", "baseValue": 8.5 },
    { "attributeName": "CON +2", "baseValue": 7.2 },
    { "attributeName": "AGI +1", "baseValue": 4.1 }
  ],

  // Calculated Metrics
  "totalFep": 19.8,
  "chaTrash": 0.0,
  "usefulFep": 19.8,
  "purity": 1.0,  // 100% pure!

  "fighterScore": 27.85,  // (8.5×1.5 + 7.2×1.5 + 4.1) = 27.85
  "crafterScore": 0.0,
  "fighterPercent": 100,
  "crafterPercent": 0,

  "powerScore": 13.93,
  "efficiency": 4.64,  // 13.93 / 3

  "tier2Count": 2,
  "hasPsy": false,
  "tierValue": 2.0,  // Base + 1.0 (2 tier +2) + 0.5 (100% purity) = clamped to 2.0
  "tier": "Best"
}
```

---

## Changelog

### Version 2.1 (2025-11-25)
- **Data Quality Filter**: Added `Hunger > 0` filter to exclude 11,010 incomplete food entries
- **Debounced Auto-Search**: Search triggers automatically as you type (300ms delay)
- **Client-Side Group Pagination**: Pagination now operates on food groups, not individual foods
- **Race Condition Fix**: Stale search results are ignored using search ID tracking
- **SQLite Compatibility**: Search uses `EF.Functions.Like()` for proper case-insensitive matching
- **JSON Deserialization Fix**: Added `PropertyNameCaseInsensitive` for API response parsing
- Valid food count reduced from 49,327 to ~38,317 (only foods with calculable efficiency)

### Version 2.0 (2025-11-23)
- Major UI overhaul with food grouping
- Added FoodGroupPanel, FoodEfficiencyCell, FoodFepsBar components
- Moved efficiency calculation to FoodHelpers.cs
- Added role-based food recommendations (Fighter, Crafter, Universal)
- Added Stat Research panels for each stat
- Added Feast Planner feature

### Version 1.0 (2025-11-22)
- Initial production release
- Core algorithm implemented and tested with 49,327 foods
- Fixed critical tier calculation issues (pagination, single food view)
- Added case-insensitive FEP parsing with logging
- Comprehensive documentation completed

---

## Credits

**Algorithm Design:** Research-driven analysis of ~38,317 Haven & Hearth foods
**Implementation:** FoodMetricsCalculator.cs, FoodService.cs, FoodHelpers.cs
**Testing:** Database queries, build verification, edge case analysis
**Documentation:** This file (1000+ lines)

---

**Questions? Issues?**
See implementation in `src/HnHMapperServer.Services/Services/FoodMetricsCalculator.cs`
Or review test results in database via SQLite queries

**Happy Cooking! 🍳**
