# PulpMX Fantasy - Product Requirements Document

**Version:** 1.0
**Last Updated:** January 10, 2026
**Product Type:** Fantasy Sports Prediction & Team Optimization Tool

---

## Table of Contents

1. [Product Overview](#product-overview)
2. [Problem Statement](#problem-statement)
3. [Target Users](#target-users)
4. [Core Features](#core-features)
5. [Fantasy Scoring Rules](#fantasy-scoring-rules)
6. [Event Structure & Race Format](#event-structure--race-format)
7. [Team Building Rules](#team-building-rules)
8. [ML Prediction System](#ml-prediction-system)
9. [User Interface](#user-interface)
10. [Success Metrics](#success-metrics)
11. [Future Enhancements](#future-enhancements)
12. [Appendix: Glossary](#appendix-glossary)

---

## Product Overview

PulpMX Fantasy is a decision-support tool for players of the PulpMX Fantasy Supercross/Motocross game. The application predicts optimal team selections by:

1. **Fetching live event data** from the PulpMX Fantasy API (rider lists, handicaps, qualifying results)
2. **Predicting fantasy point outcomes** using machine learning models trained on historical race data
3. **Optimizing team selection** under complex constraints (All-Star requirements, consecutive pick restrictions)
4. **Displaying predictions** for all riders to help users make informed decisions

### Key Value Propositions

- **Data-Driven Decisions**: ML-powered predictions based on 2+ years of historical race data
- **Constraint Handling**: Automatically enforces complex team-building rules
- **Real-Time Updates**: Syncs with PulpMX API to get latest rider information
- **Discipline-Specific**: Separate predictions for Supercross vs. Motocross (different rider skill sets)

---

## Problem Statement

### The Challenge

PulpMX Fantasy requires players to select 8 riders (4 from 250cc class, 4 from 450cc class) from a field of 40-80 riders per event. Optimal selection requires:

1. **Understanding complex scoring rules:**
   - Handicap adjustments that modify finish positions
   - All-Star designation that prevents point doubling
   - Point doubling for non-All-Stars finishing in adjusted top 10

2. **Analyzing historical performance:**
   - Rider form trends (improving vs. declining)
   - Track-specific history (some riders excel at certain venues)
   - Series-specific skills (Supercross specialists vs. Motocross specialists)

3. **Navigating constraints:**
   - Must select exactly 1 All-Star per class
   - Cannot reuse riders from previous event in same series
   - Must predict who will make the 22-rider main event (DNQ = 0 points)

4. **Processing information at scale:**
   - 80+ riders x multiple features = hundreds of data points to analyze
   - Limited time to make decisions before event registration closes

### Current State (Without This Tool)

Users must manually:
- Track each rider's recent performance
- Calculate hypothetical fantasy points under various scenarios
- Remember which riders they picked in previous events
- Guess who will qualify for the main event

**Result:** Suboptimal team selections, missed opportunities, rule violations

---

## Target Users

### Primary User: Fantasy Player

**Demographics:**
- Age: 18-55
- Location: Primarily United States
- Interest: Supercross/Motocross racing fan, competitive fantasy sports player

**Behaviors:**
- Plays PulpMX Fantasy regularly (weekly during racing season)
- Follows race results and rider news
- Wants to improve their fantasy standings
- Willing to use data tools to gain competitive advantage

**Pain Points:**
- Too many riders to analyze manually
- Difficulty understanding handicap impact on scoring
- Forgetting consecutive pick restrictions
- Uncertainty about who will qualify for main event

**Goals:**
- Win fantasy leagues and compete for prizes
- Make data-driven team selections
- Save time on team building (currently 30-60 minutes per event)
- Understand why certain riders are good picks

---

## Core Features

### 1. Event Synchronization

**Description:** Automatically fetch and display upcoming race event data

**Functionality:**
- Connect to PulpMX Fantasy API
- Retrieve next event details (venue, date, rider list)
- Display handicaps, All-Star designations, qualifying results
- Update data periodically (manual refresh + background sync)

**User Value:** Always have current information without manually checking multiple sources

---

### 2. Fantasy Point Prediction

**Description:** Predict expected fantasy points for ALL riders in an event

**Functionality:**
- Multi-stage ML pipeline:
  - **Stage 1:** Predict probability of making 22-rider main event
  - **Stage 2:** Predict finish position (1-22) if qualified
  - **Stage 3:** Apply scoring rules (handicap, doubling) deterministically
- Calculate expected value: `P(makes main) x predicted_fantasy_points`
- Provide confidence intervals (lower/upper bounds)
- Display predictions sorted by expected points

**User Value:** See which riders are statistically likely to score high

**Example Output:**
```
450 CLASS - Predicted Finish Order
Rank  Rider              Handicap  All-Star  Pred. Pts  Confidence
1     Chase Sexton       +1        Yes       45.2       High
2     Jett Lawrence      0         No        42.8       High
3     Eli Tomac          +2        Yes       38.4       Medium
...
```

---

### 3. Optimal Team Generation

**Description:** Automatically select the best 8-rider team given constraints

**Functionality:**
- Maximizes total expected fantasy points
- Enforces rules:
  - Exactly 4 riders per class (250, 450)
  - Exactly 1 All-Star per class
  - Exclude riders picked in previous event (same series)
- Handle edge cases:
  - Rider scratched from event
  - Insufficient All-Star options
  - All top riders already used

**User Value:** Get mathematically optimal team without manual trial-and-error

**Example Output:**
```
OPTIMAL TEAM (Expected: 287.4 points)

450 Class:
  Chase Sexton (All-Star)     45.2 pts
  Jett Lawrence               42.8 pts
  Cooper Webb                 38.6 pts
  Dylan Ferrandis             35.1 pts

250 Class:
  Haiden Deegan (All-Star)    44.3 pts
  Chance Hymas                40.2 pts
  Tom Vialle                  38.8 pts
  Jo Shimoda                  36.4 pts
```

---

### 4. Historical Data Import

**Description:** Import past race results to train ML models

**Functionality:**
- Bulk import from PulpMX API (by season or event slug)
- Parse race results (finish positions, fantasy points)
- Calculate derived features (average finish, momentum, track history)
- Store for model training

**User Value:** Better predictions through larger training datasets

---

### 5. Model Training & Management

**Description:** Train and update ML models with latest race data

**Functionality:**
- Train 4 models (2 per bike class):
  - Qualification model (binary classification)
  - Finish position model (regression)
- Series-specific training (Supercross vs. Motocross)
- Model versioning with metrics (AUC, R-squared, MAE)
- Hot-reload models without application restart
- Display model performance metrics

**User Value:** Continuously improving predictions as more race data accumulates

---

## Fantasy Scoring Rules

### Race Structure

**Event Day Timeline:**
1. **Daytime Qualifying (Timed Sessions)**
   - Determines starting positions for night races
   - Not directly scored in fantasy

2. **Night Show - Heat Races**
   - 20 rider heat races for each class
   - Top 9 finishers advance to main event

3. **Night Show - Last Chance Qualifier (LCQ)**
   - Remaining riders compete for final main event spots
   - Top 4 riders advance to main event

4. **Main Event (THE ONLY FANTASY SCORING RACE)**
   - 22 riders total
   - 20 laps (Supercross) or 30 minutes + 2 laps (Motocross)
   - Fantasy points awarded based on finish position + handicap

### Fantasy Points Table

**Base Points by Finish Position:**

| Position | Points | Position | Points | Position | Points |
|----------|--------|----------|--------|----------|--------|
| 1st      | 25     | 8th      | 14     | 15th     | 7      |
| 2nd      | 22     | 9th      | 13     | 16th     | 6      |
| 3rd      | 20     | 10th     | 12     | 17th     | 5      |
| 4th      | 18     | 11th     | 11     | 18th     | 4      |
| 5th      | 17     | 12th     | 10     | 19th     | 3      |
| 6th      | 16     | 13th     | 9      | 20th     | 2      |
| 7th      | 15     | 14th     | 8      | 21st     | 1      |
|          |        |          |        | 22nd     | 0      |

**DNQ (Did Not Qualify for main event): 0 points**

### Handicap System

**Purpose:** Level the playing field between elite and privateer riders

**How It Works:**
1. Each rider receives a handicap value (typically -6 to +19)
2. Handicap is SUBTRACTED from actual finish to get adjusted position
3. Fantasy points awarded based on ADJUSTED position

**Formula:**
```
Adjusted Position = Actual Finish - Handicap
Fantasy Points = Points[Adjusted Position]
```

**Example:**
- Rider finishes 8th place
- Rider has +5 handicap
- Adjusted position = 8 - 5 = 3rd place
- Base points = 20 (for 3rd place)

**Handicap Ranges:**
- **Elite riders:** -6 to +2 (makes it HARDER to score)
- **Mid-pack riders:** +3 to +10
- **Privateers:** +11 to +19 (easier to score with good finish)

### All-Star Designation

**Who Qualifies:**
- Top ~10 riders in championship standings
- Designated by PulpMX at start of season/mid-season
- Typically factory-backed riders (Yamaha, Honda, Kawasaki, KTM)

**Scoring Rule:**
- **All-Stars:** Receive SINGLE points (no doubling)
- **Non-All-Stars:** Receive DOUBLE points if adjusted position is 10th or better

**Why This Rule Exists:**
- Prevents dominance by picking only top riders
- Creates strategic tradeoff (consistent elite vs. high-variance underdog)

### Point Doubling (CRITICAL RULE)

**Eligibility:**
- Rider is NOT an All-Star
- Adjusted position is 10th or better (1-10)

**Example 1 (Non-All-Star):**
- Finishes 8th, handicap +5 -> adjusted 3rd
- Base points = 20
- **DOUBLED = 40 points**

**Example 2 (All-Star):**
- Finishes 3rd, handicap +2 -> adjusted 1st
- Base points = 25
- **NOT DOUBLED = 25 points** (All-Stars never double)

**Example 3 (Non-All-Star, No Doubling):**
- Finishes 15th, handicap +3 -> adjusted 12th
- Base points = 10
- **NOT DOUBLED = 10 points** (adjusted > 10)

### Edge Case: DNQ (Did Not Qualify)

**Causes:**
- Failed to advance from heat race
- Failed to advance from LCQ
- Mechanical DNF before main event
- Rider scratch/injury

**Scoring:** 0 fantasy points (regardless of handicap or All-Star status)

**Impact on Fantasy:** High-risk picks (privateers with good handicaps) may DNQ frequently

---

## Event Structure & Race Format

### Standard Supercross Format

**Race Day Structure:**
1. **Morning Practice** (8am-10am) - not fantasy relevant
2. **Qualifying Sessions** (1pm-4pm)
   - Timed laps, best time determines seeding
   - Results displayed as "Combined Qualy Position"
3. **Night Show** (6pm-10pm)
   - Heat Races (2 per class, top 9 to main)
   - LCQ/Semi (top 4 to main)
   - Main Event (22 riders, 20 laps)

**Field Size:**
- 250 class: ~40 riders total -> 22 make main
- 450 class: ~40 riders total -> 22 make main
- Total: ~80 riders across both classes

### Triple Crown Format (Variation)

**Differences:**
- 3 shorter main events per class (instead of 1 long main)
- Fantasy points based on OVERALL position across all 3 races
- Lower handicap limits (less variance)
- FFL (First-to-Finish-Line) rules: +15 if leads any lap 1

### Motocross Format (Outdoor)

**Structure:**
- 2 motos per class (instead of single main)
- Moto 1: Saturday morning
- Moto 2: Saturday afternoon
- Fantasy points awarded PER MOTO (2 separate scores)

**Differences from Supercross:**
- Longer races (30 min + 2 laps vs. 15 min)
- Outdoor terrain (natural vs. man-made)
- No heat races (all riders start in Moto 1)
- Rider endurance matters more

### Series Types (CRITICAL for ML)

**Why This Matters:**
Riders have different skill sets for different disciplines. Performance does not transfer between series types.

**Supercross (Indoor Stadium Racing):**
- Tight, technical tracks with jumps and whoops
- 17 rounds (January - May)
- Examples: Anaheim, Daytona, Las Vegas
- Specialists: Riders with strong technical skills, agility

**Motocross (Outdoor Racing):**
- Natural terrain tracks, longer races
- 12 rounds (May - August)
- Examples: Hangtown, Southwick, Washougal
- Specialists: Riders with endurance, outdoor experience

**SuperMotocross (Playoff):**
- 3 playoff rounds (September)
- Combines top riders from both series
- Mix of stadium and outdoor tracks

**ML Implication:**
- Train separate models per series type
- Use Supercross history to predict Supercross events
- Use Motocross history to predict Motocross events
- DO NOT MIX SERIES DATA

---

## Team Building Rules

### Team Composition Requirements

**Must Have:**
- Exactly 4 riders from 250cc class
- Exactly 4 riders from 450cc class
- Exactly 1 All-Star from 250 class
- Exactly 1 All-Star from 450 class
- Total: 8 riders (2 All-Stars, 6 non-All-Stars)

### Consecutive Pick Restriction (CRITICAL)

**Rule:** Cannot pick the same rider in consecutive rounds of the SAME series

**Example:**
- Round 5 (Anaheim 2) - Pick Jett Lawrence
- Round 6 (San Diego) - CANNOT pick Jett Lawrence
- Round 7 (Arlington) - CAN pick Jett Lawrence again

**Why This Rule Exists:**
- Prevents dominant strategy of always picking same elite riders
- Encourages roster diversity week-to-week
- Creates strategic depth (when to "burn" a top rider)

**Series Boundaries:**
- Restriction resets when new series starts
- Example: Pick Jett in last Supercross round -> CAN pick him Round 1 of Motocross

### Optional Picks

**First-to-Finish-Line (FFL):**
- Pick 1 rider per class to finish on podium (top 3)
- Correct guess: +15 points
- Wrong guess: -7 points
- Expected value calculation: P(top 3) x 15 - P(not top 3) x 7

**Not Implemented Yet (Future Feature)**

---

## ML Prediction System

### Overview

The system uses a multi-stage machine learning pipeline to predict fantasy points for each rider. This approach models the actual sequence of events that determine fantasy scoring.

### Stage 1: Qualification Prediction

**Goal:** Predict probability that a rider makes the 22-rider main event

**Factors Considered:**
- Historical finish rate (% of races finished)
- Recent performance (last 5 races)
- Handicap value (proxy for skill level)
- Track-specific history
- All-Star designation

**Output:** Probability from 0.0 to 1.0

### Stage 2: Finish Position Prediction

**Goal:** Predict where rider finishes (1-22) IF they make the main

**Factors Considered:**
- Historical average finish
- Recent momentum (trending up or down)
- Track-specific history
- Championship points standing
- Handicap value

**Output:** Predicted finish position (1-22)

### Stage 3: Fantasy Points Calculation

**Goal:** Apply fantasy scoring rules to predicted finish

**Process:**
1. Apply handicap adjustment to predicted finish
2. Look up base points from scoring table
3. Apply doubling rule if eligible
4. Multiply by qualification probability

**Output:** Expected fantasy points with confidence interval

### Series-Specific Training

**Problem:** Mixing Supercross and Motocross data dilutes predictions

**Solution:**
- Train separate models for Supercross events
- Train separate models for Motocross events
- Use only same-series history when predicting

**Rationale:**
- Supercross specialists may struggle outdoors
- Motocross specialists may struggle on tight stadium tracks
- Using appropriate history improves signal-to-noise ratio

### Confidence Levels

**High Confidence:**
- Rider has extensive historical data
- Consistent recent performance
- Track history available

**Medium Confidence:**
- Some historical data available
- Moderate consistency
- Limited track history

**Low Confidence:**
- Little or no historical data
- Highly variable recent performance
- No track history

---

## User Interface

### Page Structure

#### 1. Home Dashboard (`/`)

**Purpose:** Show next upcoming event and quick actions

**Elements:**
- Next event card (name, date, venue, lockout time)
- Quick stats (events synced, predictions available)
- Action buttons: "View Predictions", "Generate Optimal Team"

#### 2. Predictions Page (`/predictions`)

**Purpose:** Display ALL rider predictions for next event

**Layout:**
- Two tabs: 250 Class | 450 Class
- Sortable table with columns:
  - Rank (by expected points)
  - Rider Name
  - Number
  - Handicap
  - All-Star (yes/no)
  - Predicted Finish
  - Points if Qualifies
  - Expected Points
  - Confidence (High/Medium/Low)
  - Range (lower - upper bound)

**Features:**
- Sort by column
- Search/filter by rider name
- Color coding by confidence level

#### 3. Optimal Team Page (`/teams/optimal`)

**Purpose:** Display algorithmically-generated optimal team

**Layout:**
- Team summary (total expected points)
- Two class sections (450, 250)
- For each rider: name, number, handicap, All-Star designation, expected points

**Features:**
- "Copy Team" button (format for easy pasting)
- "What-if" analysis (exclude specific riders, regenerate)

#### 4. Admin Panel (`/admin`)

**Purpose:** Data management and model training (restricted access)

**Features:**
- "Sync Next Event" button
- "Import Historical Data" form (season selection)
- "Train ML Models" button with progress tracking
- Command status table showing recent operations

---

## Success Metrics

### User Engagement Metrics

**Primary:**
- **Active Users per Week** - How many users generate predictions/teams weekly
- **Predictions Generated** - Total prediction requests per event
- **Optimal Teams Created** - How many users use the team optimizer

**Secondary:**
- **Return Rate** - % of users who return for next event
- **Time Spent** - Average session duration (target: 5-10 min vs. 30-60 min manual)

### Prediction Accuracy Metrics

**Qualification Model:**
- **Accuracy** - % of correct main event qualifications (target: 75-85%)
- **AUC-ROC** - Discrimination ability (target: 0.80-0.85)
- **Precision** - Of riders predicted to qualify, % who actually did (target: 85%)

**Finish Position Model:**
- **R-squared** - Variance explained (target: 0.30-0.50)
- **MAE** - Mean absolute error in positions (target: 3-5 positions)
- **Top-3 Accuracy** - % of predicted top-3 riders who finish top-3 (target: 40-60%)

**Expected Points Accuracy:**
- **Correlation** - Pearson correlation between predicted and actual fantasy points (target: 0.60+)
- **Top Picks Performance** - Do top-predicted riders actually score high? (target: top 10 predicted -> top 15 actual)

### Model Improvement Metrics

- **Training Data Growth** - # of historical events in database (more = better models)
- **Retraining Frequency** - How often models updated with new data (target: weekly)
- **Model Version Performance** - Does each new model improve on previous? (track R-squared over time)

---

## Future Enhancements

### Phase 2 Features (Not Implemented Yet)

1. **First-to-Finish-Line (FFL) Prediction**
   - Predict probability of top-3 finish
   - Calculate expected value (+15 vs. -7)
   - Include in optimal team algorithm

2. **User Accounts & Authentication**
   - Save team history per user
   - Track consecutive picks per user
   - Personalized recommendations

3. **Alternative Team Suggestions**
   - Show 2nd, 3rd best teams
   - Risk-averse vs. risk-seeking strategies
   - Budget constraints (salary cap variation)

4. **Rider Comparison Tool**
   - Side-by-side rider stats
   - Head-to-head historical matchups
   - "Who should I pick?" wizard

5. **Mobile App**
   - Native iOS/Android app
   - Push notifications for event reminders
   - Offline mode with cached predictions

6. **Live Race Updates**
   - Real-time scoring during race
   - Update predictions based on live qualifying
   - "Should I change my picks?" alerts

7. **Advanced Analytics**
   - Track personal fantasy performance over season
   - Compare against other users
   - Identify weak spots in team selection

8. **Social Features**
   - Share team picks
   - League management

---

## Appendix: Glossary

**Terms:**
- **DNQ** - Did Not Qualify (failed to make 22-rider main event)
- **LCQ** - Last Chance Qualifier (race to earn final main event spots)
- **All-Star** - Top-tier riders who don't receive point doubling
- **Handicap** - Adjustment value subtracted from finish position
- **Adjusted Position** - Finish position minus handicap (used for scoring)
- **Point Doubling** - Non-All-Stars in adjusted top-10 get 2x points
- **Series** - Full season of racing (17 Supercross rounds, 12 Motocross rounds, etc.)
- **Round** - Individual race event within a series
- **Privateer** - Non-factory-backed rider (typically higher handicap)
- **Factory Team** - Manufacturer-backed team (Yamaha, Honda, Kawasaki, KTM)
- **Lockout Time** - Deadline after which team picks cannot be changed (typically race start time)

---

**End of Product Requirements Document**
