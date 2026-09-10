# Each defect is applied only to an ignored copy; production source remains unchanged.
param([string]$CaseFilter = '', [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root ('_test\dollar-sabotage-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'src') -Destination $scratch -Recurse
$cases = @(
    @{ Name='backdrop follower overrides base'; File='Dock.cs'; Suite='backdrop'; Old='if (basis != null) return Math.Max(4, Thick(basis) - basis.BarOverhangPx);'; New='if (false) return Math.Max(4, Thick(basis) - basis.BarOverhangPx);' },
    @{ Name='backdrop follower paints own width'; File='Dock.cs'; Suite='backdrop'; Old='double own = b.BarOverhangPx > 0 ? Thick(b) : floor;'; New='double own = Thick(b);' },
    @{ Name='backdrop quote gradient seam'; File='WidgetWindow.cs'; Suite='backdrop'; Old='Background = Palette.CardEnd,'; New='Background = Palette.Card,' },
    @{ Name='backdrop attached gradient seam'; File='PanelWindow.cs'; Suite='backdrop'; Old='if (DockStack.HasCompanion(this)) return Palette.CardEnd;'; New='if (DockStack.HasCompanion(this)) return Palette.Card;' },
    @{ Name='cli ignores desktop installation'; File='DollarSpark.cs'; Suite='cli-side'; Old='if (Directory.Exists(desktop)) foreach'; New='if (false) foreach' },
    @{ Name='cli selects older version'; File='DollarSpark.cs'; Suite='cli-side'; Old='version > latest'; New='version < latest' },
    @{ Name='cli effort label differs from call'; File='DollarSpark.cs'; Suite='cli-side'; Old='internal const string ReasoningEffort = "high";'; New='internal const string ReasoningEffort = "medium";' },
    @{ Name='cli effort hidden after model swap'; File='DollarAnalysisWindow.cs'; Suite='dollar-layout'; Old='_sparkLabel.Text = DollarSpark.ModelLabel(model) + " ▾";'; New='_sparkLabel.Text = DollarSpark.ModelName(model) + " ▾";' },
    @{ Name='login status read from stdout only'; File='DollarSpark.cs'; Suite='dollar'; Old='text = withDiagnostics ? output.ToString()'; New='text = false ? output.ToString()' },
    @{ Name='scorecard shows a retired mode name'; File='PredictionJournal.cs'; Suite='docs'; Old='key[0] == "extreme-rule" ? "AI 전망(규칙 대체)"'; New='key[0] == "extreme-rule" ? "극단 규칙"' },
    @{ Name='scorecard split by release instead of yardstick'; File='PredictionJournal.cs'; Suite='journal'; Old='string key = model + "|" + h + "|" + Config.ScoringEra(root.GetAttribute("version"));'; New='string key = model + "|" + h + "|" + root.GetAttribute("version");' },
    @{ Name='every release starts its own scoring era'; File='Config.cs'; Suite='json'; Old='if (rv > 0 && rv <= v) era = r;'; New='if (rv > 0 && rv <= v) era = version;' },
    @{ Name='weather opens a prediction window'; File='DollarAnalysisWindow.cs'; Suite='json'; Old='if (def == null || def.Kind == SourceKind.Weather) return;'; New='if (def == null) return;' },
    @{ Name='weather can be added to the quote list'; File='WidgetWindow.cs'; Suite='json'; Old='if (def == null || def.Kind == SourceKind.Weather) return;'; New='if (def == null) return;' },
    @{ Name='quote search stops filtering weather'; File='SearchWindow.cs'; Suite='docs'; Old='hits = hits.Where(h => h.Def == null || h.Def.Kind != SourceKind.Weather).ToList();'; New='hits = hits.ToList();' },
    @{ Name='side bar does not reserve room for favourites'; File='WidgetWindow.cs'; Suite='cli-side'; Old='return (vertical ? _dockApps.DesiredSize.Height : _dockApps.DesiredSize.Width) + 8;'; New='return vertical ? 0 : _dockApps.DesiredSize.Width + 8;' },
    @{ Name='side bar section order ignored'; File='WidgetWindow.cs'; Suite='cli-side'; Old='? new UIElement[] { _dockApps, _dockClip }'; New='? new UIElement[] { _dockClip, _dockApps }' },
    @{ Name='weather and clock float up with the quotes'; File='WidgetWindow.cs'; Suite='cli-side'; Old='VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, SideGap),'; New='VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 0, SideGap),' },
    @{ Name='side name center offset'; File='WidgetWindow.cs'; Suite='cli-side'; Old='if (centered) name.Margin = new Thickness(12, 0, 0, 0);'; New='if (centered) name.Margin = new Thickness(0);' },
    @{ Name='side width change ignored'; File='WidgetWindow.cs'; Suite='cli-side'; Old=".Append('x').Append(Math.Round(vertical ? (double.IsNaN(Width) ? ActualWidth : Width) : (double.IsNaN(Height) ? ActualHeight : Height), 2))"; New=".Append('x')" },
    @{ Name='primary basic promoted to main'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='var host = primary ? _primaryHost : _comparisonBody;'; New='var host = _primaryHost;' },
    @{ Name='primary micro moves called directional'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='int direction = DollarAnalysis.DirectionAt(change.Value, DollarAnalysis.Threshold(horizon, result.RoundTripPercent));'; New='int direction = Math.Sign(change.Value);' },
    @{ Name='primary periods use daily threshold'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='int direction = DollarAnalysis.DirectionAt(change.Value, DollarAnalysis.Threshold(horizon, result.RoundTripPercent));'; New='int direction = DollarAnalysis.DirectionAt(change.Value, DollarAnalysis.Threshold(1, 0));' },
    @{ Name='primary counters hidden'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='_periodChecks[index].Visibility = Visibility.Visible;'; New='_periodChecks[index].Visibility = Visibility.Collapsed;' },
    @{ Name='primary comparison competes on graph'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='if (result.Spark == null) _chart.Children.Add(line);'; New='_chart.Children.Add(line);' },
    @{ Name='primary dots use past history'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='foreach (var point in result.Spark != null ? integrated.Points : line.Points)'; New='foreach (var point in line.Points)' },
    @{ Name='primary comparison collapsed by default'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='Header = "비교 기준 · 규칙·과거 통계", IsExpanded = true'; New='Header = "비교 기준 · 규칙·과거 통계", IsExpanded = false' },
    @{ Name='primary rejected response called success'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='if (suppliedAi && result.Spark == null)'; New='if (false)' },
    @{ Name='placement saved monitor overrides owner'; File='DollarAnalysisWindow.cs'; Suite='placement'; Old='if (_positionedNearOwner) return;'; New='if (false) return;' },
    @{ Name='placement primary monitor forced'; File='DollarAnalysisWindow.cs'; Suite='placement'; Old='Dock.ScreenAt(all, clicked ? pointer : new Point(ownerPx.Left + ownerPx.Width / 2, ownerPx.Top + ownerPx.Height / 2));'; New='all[0];' },
    @{ Name='placement work area clamp removed'; File='DollarAnalysisWindow.cs'; Suite='placement'; Old='x = Math.Max(work.Left + margin, Math.Min(x, work.Right - margin - width));'; New='x += 0;' },
    @{ Name='placement bar click ignored'; File='DollarAnalysisWindow.cs'; Suite='placement'; Old='x = clicked.HasValue ? clicked.Value.X - 40 : owner.Left;'; New='x = owner.Left;' },
    @{ Name='shared quote alert lost'; File='WidgetWindow.cs'; Suite='surge'; Old='if (surge != 0) FlashSurge('; New='if (false) FlashSurge(' },
    @{ Name='five minute alert drops latency'; File='WidgetWindow.cs'; Suite='surge'; Old='Math.Max(SurgeMaxGapSec, _cfg.QuoteIntervalSec + 30)'; New='SurgeMaxGapSec' },
    @{ Name='surge uses rounded price'; File='WidgetWindow.cs'; Suite='surge'; Old='double now = PredictionTarget.Number(q);'; New='double now = ParsePrice(q.Price);' },
    @{ Name='collapsed surge surface lost'; File='WidgetWindow.cs'; Suite='surge'; Old='FlashBackground(_collapsedSurge, Palette.Clear);'; New='System.GC.KeepAlive(_collapsedSurge);' },
    @{ Name='strength index keeps partial days'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (pair.Value.Count != StrengthQuotes.Length) continue;'; New='if (pair.Value.Count < 1) continue;' },
    @{ Name='strength index uses an arithmetic mean'; File='DollarAnalysis.cs'; Suite='deep'; Old='foreach (double v in pair.Value.Values) sum += Math.Log(v);'; New='foreach (double v in pair.Value.Values) sum += v;' },
    @{ Name='strength index skips normalisation'; File='DollarAnalysis.cs'; Suite='deep'; Old='foreach (var r in raw) r.Value = r.Value / baseline * 100;'; New='foreach (var r in raw) r.Value = r.Value * 1;' },
    @{ Name='strength index reads the future'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (date > today) continue;                  // 아직 오지 않은 날'; New='if (false) continue;                  // 아직 오지 않은 날' },
    @{ Name='strength index accepts a conflicting rate'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (byDate[date].ContainsKey(quote) && byDate[date][quote] != value) return empty;'; New='if (false) return empty;' },
    @{ Name='strength index accepts a bad number'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0 || value > 100000) return empty;'; New='if (false) return empty;' },
    @{ Name='strength change reports zero instead of unknown'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (days < 1 || DollarIndex.Count <= days) return double.NaN;'; New='if (days < 1 || DollarIndex.Count <= days) return 0;' },
    @{ Name='strength index speaks too early'; File='DollarAnalysis.cs'; Suite='deep'; Old='public bool Ok { get { return DollarIndex.Count >= 25; } }'; New='public bool Ok { get { return DollarIndex.Count >= 1; } }' },
    @{ Name='strength request asks beyond today'; File='DollarAnalysis.cs'; Suite='deep'; Old='"&to=" + today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);'; New='"&to=" + today.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);' },
    @{ Name='empty strength index sent to the model as real'; File='DollarSpark.cs'; Suite='deep'; Old='else b.Append("null");     // 달러 강세 지수가 없을 때'; New='else b.Append("{}");     // 달러 강세 지수가 없을 때' },
    @{ Name='model never shown the strength index'; File='DollarSpark.cs'; Suite='deep'; Old='if (result.Context != null && result.Context.Ok)'; New='if (false)' },
    @{ Name='displayed flat band ignores cost'; File='DollarAnalysisWindow.cs'; Suite='deep'; Old='PeriodName(h) + " ±" + (DollarAnalysis.Threshold(h, roundTripPercent) * 100)'; New='PeriodName(h) + " ±" + (DollarAnalysis.Threshold(h, 0) * 100)' },
    @{ Name='receding expectation scored as a cut'; File='DollarFactors.cs'; Suite='dollar'; Old='if (cut != hike && Receding(text))'; New='if (false)' },
    @{ Name='receding expectation erased by its own negation'; File='DollarFactors.cs'; Suite='dollar'; Old='"전망 변경은 실제 금리 변경이 아닙니다. 시장이 먼저 반영했는지 확인해야 합니다.", 0.4, true);'; New='"전망 변경은 실제 금리 변경이 아닙니다. 시장이 먼저 반영했는지 확인해야 합니다.", 0.4);' },
    @{ Name='negation flag ignored'; File='DollarFactors.cs'; Suite='dollar'; Old='bool negated = !eventIsAbsence && Has(text,'; New='bool negated = Has(text,' },
    @{ Name='bare easing counted as a rate cut'; File='DollarFactors.cs'; Suite='dollar'; Old='(?:monetary|policy|rate|rates|credit)\s+easing|eas(?:e|es|ed|ing)'; New='easing|eas(?:e|es|ed|ing)' },
    @{ Name='bare tightening counted as a rate hike'; File='DollarFactors.cs'; Suite='dollar'; Old='(?:monetary|policy|rate|rates|credit)\s+tightening|tighten(?:s|ed|ing)?'; New='tightening|tighten(?:s|ed|ing)?' },
    @{ Name='a warning weighted like a decision'; File='DollarFactors.cs'; Suite='dollar'; Old='|urges?|threatens?|signals?|warns?|warned|warning'; New='|urges?|threatens?|signals?' },
    @{ Name='non-policy rate read as a Fed decision'; File='DollarFactors.cs'; Suite='dollar'; Old='return Has(s, FedActor) || (Has(s, @"미국.{0,8}금리") && !Has(s, NonPolicyRate));'; New='return Has(s, Fed);' },
    @{ Name='cooling inflation also fires the hot rule'; File='DollarFactors.cs'; Suite='dollar'; Old='if (!Slowing(text) && Has(text, @"급등|상승|가속|웃돌|높아|\bhot'; New='if (Has(text, @"급등|상승|가속|웃돌|높아|hot' },
    @{ Name='slowing export growth read as improvement'; File='DollarFactors.cs'; Suite='dollar'; Old='if (Slowing(text)) good = bad = false;'; New='if (false) good = bad = false;' },
    @{ Name='risks read as rises'; File='DollarFactors.cs'; Suite='dollar'; All=$true; Old='ris(?:e|es|en|ing)\b'; New='ris\w*' },
    @{ Name='disagreement read as agreement'; File='DollarFactors.cs'; Suite='dollar'; Old='|\bagreements?\b|'; New='|agreement|' },
    @{ Name='no whole-sentence fallback when clauses find nothing'; File='DollarFactors.cs'; Suite='dollar'; Old='if (result.Count == 0) Scan(result, n, input);'; New='if (false) Scan(result, n, input);' },
    @{ Name='fallback runs even when evidence was found'; File='DollarFactors.cs'; Suite='dollar'; Old='if (result.Count == 0) Scan(result, n, input);'; New='Scan(result, n, input);' },
    @{ Name='attribution outside the clause ignored'; File='DollarFactors.cs'; Suite='dollar'; Old='string whole = (n.Title ?? "") + " " + text;'; New='string whole = text;' },
    @{ Name='bank name read as the central bank'; File='DollarAnalysis.cs'; Suite='dollar'; Old='(?<![가-힣])한은'; New='한은' },
    @{ Name='customs office read as tariffs'; File='DollarAnalysis.cs'; Suite='dollar'; Old='관세(?!청)'; New='관세' },
    @{ Name='policy meeting headlines dropped'; File='DollarAnalysis.cs'; Suite='dollar'; Old='금통위|이창용|연준|파월|Powell|\\bFOMC\\b|'; New='연준|' },
    @{ Name='oil matched inside another word'; File='DollarAnalysis.cs'; Suite='dollar'; All=$true; Old='\\boil\\b'; New='oil' },
    @{ Name='evidence and direction disagree in silence'; File='DollarAnalysisWindow.cs'; Suite='dollar'; Old='text += Reconcile(Outlook(score), ScenarioOutlook(result, h, now));'; New='text += "";' },
    @{ Name='regulatory relief stays silent'; File='PredictionFactors.cs'; Suite='dollar'; Old='불확실성.{0,6}(?:해소|완화)|규제.{0,8}해소|'; New='' },
    @{ Name='stablecoin surge not read as issuance'; File='PredictionFactors.cs'; Suite='dollar'; Old='발행|추가\s?발행|증가|급증|확대|늘[어었]'; New='발행|추가\s?발행|증가' },
    @{ Name='institutional buying missed'; File='PredictionFactors.cs'; Suite='dollar'; Old='(?:매입|매수|채택|편입|보유|축적)'; New='(?:매입|채택|편입|보유)' },
    @{ Name='English earnings beat scores nothing'; File='PredictionFactors.cs'; Suite='dollar'; Old='\b(?:beat|beats|tops|topped|exceeds|exceeded)\b.{0,24}\b(?:estimates?|expectations?|forecasts?|views?)\b|'; New='' },
    @{ Name='English guidance cut scores nothing'; File='PredictionFactors.cs'; Suite='dollar'; Old='\b(?:cuts?|cut|lowers?|lowered|slashes?|slashed|trims?|trimmed|scraps?|withdraws?)\b.{0,16}\b(?:outlook|guidance|forecast|target|dividend)\b|'; New='' },
    @{ Name='buyback and probe not company events'; File='PredictionFactors.cs'; Suite='dollar'; Old='영업이익|실적|매출|가이던스|자사주|배당|리콜|조사|소송|어닝|수주|압수|'; New='영업이익|실적|매출|가이던스|' },
    @{ Name='Korean company events have no direction'; File='PredictionFactors.cs'; Suite='dollar'; Old='자사주.{0,8}(?:매입|취득|소각)|배당.{0,8}(?:확대|증액|인상|상향)|수주.{0,8}(?:확대|증가|잭팟)|어닝\s?서프라이즈|'; New='' },
    @{ Name='Korean adverse events have no direction'; File='PredictionFactors.cs'; Suite='dollar'; Old='리콜|압수\s?수색|(?:검찰|공정위|금감원|국세청).{0,10}(?:조사|제재|고발|추징)|배당.{0,8}(?:축소|삭감|중단)|어닝\s?쇼크|'; New='' },
    @{ Name='ledger judges a rule from too few cases'; File='RuleSkill.cs'; Suite='deep'; Old='internal const int MinimumFires = 30;'; New='internal const int MinimumFires = 1;' },
    @{ Name='ledger baseline is not the realised majority'; File='RuleSkill.cs'; Suite='deep'; Old='commonByHorizon[group.Key] = mine.GroupBy(c => c.Outcome).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;'; New='commonByHorizon[group.Key] = 0;' },
    @{ Name='ledger pools the climatology across horizons'; File='RuleSkill.cs'; Suite='deep'; Old='int common = commonByHorizon[row.Horizon];'; New='int common = commonByHorizon.Values.Max();' },
    @{ Name='ledger treats overlapping windows as independent'; File='RuleSkill.cs'; Suite='deep'; Old='if (c.Created < end) continue;'; New='if (false) continue;' },
    @{ Name='ledger forgets which version produced the numbers'; File='RuleSkill.cs'; Suite='deep'; Old='if (!string.IsNullOrEmpty(row.Version)) r.Versions.Add(Config.ScoringEra(row.Version));'; New='' },
    @{ Name='ledger reads the outcome backwards'; File='RuleSkill.cs'; Suite='deep'; Old='int outcome = DollarAnalysis.DirectionAt(actual / anchor - 1, band);'; New='int outcome = DollarAnalysis.DirectionAt(anchor / actual - 1, band);' },
    @{ Name='ledger calls a perfect record decidable at once'; File='RuleSkill.cs'; Suite='deep'; Old='if (spread <= 0) spread = 0.25;'; New='if (spread <= 0) spread = 0;' },
    @{ Name='ledger mixes other instruments'; File='RuleSkill.cs'; Suite='deep'; Old='if (!HeaderMatches(path, identity, source)) continue;'; New='if (false) continue;' },
    @{ Name='widget never grades due predictions'; File='WidgetWindow.cs'; Suite='deep'; Old='try { PredictionJournal.Observe(quote, now); }'; New='try { }' },
    @{ Name='one bad quote stops all grading'; File='WidgetWindow.cs'; Suite='deep'; Old='if (quote == null || !quote.Ok) continue;'; New='if (quote == null || !quote.Ok) return;' },
    @{ Name='grading runs on every quote cycle'; File='WidgetWindow.cs'; Suite='deep'; Old='return (now - last).TotalSeconds >= GradeIntervalSec;'; New='return true;' },
    @{ Name='ancient records reopened every cycle'; File='PredictionJournal.cs'; Suite='deep'; Old='try { if (File.GetLastWriteTimeUtc(path) < now.AddDays(-60)) continue; }'; New='try { if (false) continue; }' },
    @{ Name='a record without a threshold breaks grading'; File='PredictionJournal.cs'; Suite='deep'; Old='double t = Maybe(forecast, "threshold");'; New='double t = N(forecast, "threshold");' },
    @{ Name='one broken record stops the whole grading pass'; File='PredictionJournal.cs'; Suite='deep'; Old='} catch (FormatException) { } catch (OverflowException) { } catch (IOException) { }'; New='} finally { }' },
    @{ Name='yields turned into an index'; File='DollarAnalysis.cs'; Suite='deep'; Old='return double.IsNaN(now) || double.IsNaN(then) ? double.NaN : now - then;'; New='return double.IsNaN(now) || double.IsNaN(then) ? double.NaN : (now / then - 1) * 100;' },
    @{ Name='curve spread pairs different days'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (Yield2Y[i].Date == last.Date) return last.Value - Yield2Y[i].Value;'; New='return last.Value - Yield2Y[i].Value;' },
    @{ Name='absurd yields accepted'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (double.IsNaN(value) || value <= 0 || value > 25) continue;'; New='if (double.IsNaN(value)) continue;' },
    @{ Name='future yields accepted'; File='DollarAnalysis.cs'; Suite='deep'; Old='if (date == DateTime.MinValue || date.Date > today || date.Date < today.AddYears(-3)) continue;'; New='if (date == DateTime.MinValue) continue;' },
    @{ Name='overlapping months double counted'; File='DollarAnalysis.cs'; Suite='deep'; Old='return all.GroupBy(r => r.Date).Select(g => g.First()).OrderBy(r => r.Date).ToList();'; New='return all.OrderBy(r => r.Date).ToList();' },
    @{ Name='yield request asks for a whole year'; File='DollarAnalysis.cs'; Suite='deep'; Old='month.ToString("yyyyMM", CultureInfo.InvariantCulture);'; New='month.ToString("yyyy", CultureInfo.InvariantCulture);' },
    @{ Name='yields speak too early'; File='DollarAnalysis.cs'; Suite='deep'; Old='internal bool YieldOk { get { return Yield10Y.Count >= 25; } }'; New='internal bool YieldOk { get { return Yield10Y.Count >= 1; } }' },
    @{ Name='empty yields sent to the model as real'; File='DollarSpark.cs'; Suite='deep'; Old='if (result.Context != null && result.Context.YieldOk)'; New='if (false)' },
    @{ Name='horizon weight silently changed'; File='DollarAnalysis.cs'; Suite='dollar'; Old='internal static double HorizonWeight(int horizon) { return horizon == 20 ? 0.25 : horizon == 5 ? 0.5 : 1; }'; New='internal static double HorizonWeight(int horizon) { return 1; }' },
    @{ Name='ceiling ignores the horizon'; File='DollarAnalysis.cs'; Suite='dollar'; Old='return (80 * HorizonWeight(horizon) + 20 * 0.25) * reliability;'; New='return (80 + 20 * 0.25) * reliability;' },
    @{ Name='needed score ignores the band'; File='DollarAnalysis.cs'; Suite='dollar'; Old='return span > 0 && !double.IsNaN(span) ? band / span * 100 : double.NaN;'; New='return span > 0 && !double.IsNaN(span) ? 100 / span : double.NaN;' },
    @{ Name='screen hides a horizon that cannot speak'; File='DollarAnalysisWindow.cs'; Suite='dollar'; Old='if (!double.IsNaN(headroom) && headroom >= 50)'; New='if (false)' },
    @{ Name='eas matches inside increase'; File='DollarFactors.cs'; Suite='dollar'; All=$true; Old='\beas(?:e|es|ed|ing)\b'; New='eas\w*' },
    @{ Name='miss matches inside mission'; File='DollarFactors.cs'; Suite='dollar'; Old='\bmiss(?:es|ed|ing)?\b'; New='miss\w*' },
    @{ Name='hot matches inside photo'; File='DollarFactors.cs'; Suite='dollar'; Old='\bhot(?:ter|test)?\b'; New='hot\w*' },
    @{ Name='fall matches inside windfall'; File='DollarFactors.cs'; Suite='dollar'; All=$true; Old='\bfall(?:s|en|ing)?\b'; New='fall\w*' },
    @{ Name='a real decision read as a receding expectation'; File='DollarFactors.cs'; Suite='dollar'; Old='if (Decided(s)) return false;'; New='if (false) return false;' },
    @{ Name='receding needs no expectation word'; File='DollarFactors.cs'; Suite='dollar'; Old='bool en = Has(s, RecedeEn) && Has(s, ExpectWordEn);'; New='bool en = Has(s, RecedeEn);' },
    @{ Name='compound tariffs dropped from the feed'; File='DollarAnalysis.cs'; Suite='dollar'; Old='관세(?!청)'; New='(?<![가-힣])관세(?!청)' },
    @{ Name='a suffix read as Iran'; File='DollarAnalysis.cs'; Suite='dollar'; Old='Trump|트럼프|Iran|(?<![가-힣])이란'; New='Trump|트럼프|Iran|이란' },
    @{ Name='a tariff taking effect has no direction'; File='DollarFactors.cs'; Suite='dollar'; Old='부과|인상|위협|발효|시행|확대'; New='부과|인상|위협' },
    @{ Name='domestic stock ledger falls back to fx'; File='RuleSkill.cs'; Suite='deep'; Old='kind == "dstock" ? SourceKind.DomesticStock'; New='kind == "stock" ? SourceKind.DomesticStock' },
    @{ Name='an unknown direction explained as flat'; File='DollarAnalysisWindow.cs'; Suite='dollar'; Old='if (direction != "상승" && direction != "하락" && direction != "보합") return "";'; New='if (false) return "";' },
    @{ Name='treasury feed back on the fast lane'; File='DollarAnalysis.cs'; Suite='docs'; Old='Net.GetSlowTextAsync(YieldUrl(m), ct)'; New='Net.GetTextAsync(YieldUrl(m), ct)' },
    @{ Name='slow cache serves stale documents forever'; File='Net.cs'; Suite='deep'; Old='if (now < hit.Key || now - hit.Key > SlowCacheLife) return null;'; New='if (false) return null;' },
    @{ Name='an empty response is remembered as data'; File='Net.cs'; Suite='deep'; Old='if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(body)) return;'; New='if (string.IsNullOrEmpty(url)) return;' },
    @{ Name='numeric context never reaches the record'; File='PredictionJournal.cs'; Suite='deep'; Old='if (r.Context != null && r.Context.Ok) {'; New='if (false) {' },
    @{ Name='treasury yield never reaches the record'; File='PredictionJournal.cs'; Suite='deep'; Old='if (r.Context != null && r.Context.YieldOk) {'; New='if (false) {' },
    @{ Name='a thin context is recorded anyway'; File='DollarAnalysis.cs'; Suite='deep'; Old='public bool Ok { get { return DollarIndex.Count >= 25; } }'; New='public bool Ok { get { return true; } }' },
    @{ Name='lost scoring windows are not counted'; File='PredictionJournal.cs'; Suite='deep'; Old='if (due.AddHours(6) < now) missed++;'; New='if (false) missed++;' },
    @{ Name='graded predictions counted as lost'; File='PredictionJournal.cs'; Suite='deep'; Old='if (File.Exists(output)) continue;'; New='if (false) continue;' },
    @{ Name='similar cases keyed on the old six-bucket feature'; File='DollarAnalysis.cs'; Suite='dollar'; Old='return change > 0.001 ? 2 : change < -0.001 ? 0 : 1;'; New='return (change > 0.001 ? 2 : change < -0.001 ? 0 : 1) * 2 + (index % 2);' },
    @{ Name='similar cases ignore the previous-day sign'; File='DollarAnalysis.cs'; Suite='dollar'; Old='double change = rates[index].Value / rates[index - 1].Value - 1;'; New='double change = 0;' },
    @{ Name='calibration trains on future'; File='ProbabilityCalibration.cs'; Suite='deep'; Old='c.Resolved <= current.At && c.At < current.At'; New='true' },
    @{ Name='calibration counts overlapping data'; File='ProbabilityCalibration.cs'; Suite='deep'; Old='c.At < end ||'; New='false ||' },
    @{ Name='calibration insufficient test accepted'; File='ProbabilityCalibration.cs'; Suite='deep'; Old='result.Count >= 30'; New='result.Count >= 1' },
    @{ Name='journal counts overlapping predictions'; File='PredictionJournal.cs'; Suite='deep'; Old='if (row.Item1 < end) continue;'; New='if (false) continue;' },
    @{ Name='closed market accepted for grading'; File='PredictionJournal.cs'; Suite='deep'; Old='q == null || !q.Ok || q.MarketClosed ||'; New='q == null || !q.Ok ||' },
    @{ Name='flat grading loses threshold'; File='PredictionJournal.cs'; Suite='deep'; Old='DollarAnalysis.DirectionAt(predicted / anchor - 1, band) == DollarAnalysis.DirectionAt(actual / anchor - 1, band)'; New='Math.Sign(predicted - anchor) == Math.Sign(actual - anchor)' },
    @{ Name='past price double counted'; File='PredictionFactors.cs'; Suite='deep'; Old='if (up != down && forwardEvent)'; New='if (up != down)' },
    @{ Name='prediction double click suppressed'; File='WidgetWindow.cs'; Suite='all'; Old='CancelPress(); _predictionOpen(def);'; New='CancelPress();' },
    @{ Name='valid duplicate not merged'; File='DollarSpark.cs'; Suite='all'; Old='if (existing != null) {'; New='if (false) {' },
    @{ Name='quote arrows reversed'; File='DollarAnalysisWindow.cs'; Suite='all'; Old='ratio > 0 ? "▲ " : "▼ "'; New='ratio > 0 ? "▼ " : "▲ "' },
    @{ Name='quote percent detached'; File='DollarAnalysisWindow.cs'; Suite='all'; Old='var priceRow = new Grid { HorizontalAlignment = HorizontalAlignment.Left };'; New='var priceRow = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };' },
    @{ Name='brief conclusion hidden'; File='DollarFactors.cs'; Suite='all'; Old='"단기 비교: " + outlook'; New='"" + outlook' },
    @{ Name="journal premature score"; File="PredictionJournal.cs"; Suite="journal"; Old="q.ReceivedUtc < due ||"; New="false ||" },
    @{ Name="journal late score"; File="PredictionJournal.cs"; Suite="journal"; Old="q.ReceivedUtc > due.AddHours(6)"; New="false" },
    @{ Name="journal mixed feed"; File="PredictionJournal.cs"; Suite="journal"; Old='&& (reader.GetAttribute("source") ?? "") == (source ?? "");'; New='&& true;' },
    @{ Name="Astra routed to Spark"; File="DollarSpark.cs"; Suite="journal"; Old='ModelId(model) + " -c model_reasoning_effort'; New='Model + " -c model_reasoning_effort' },
    @{ Name='Spark allowed in basic mode'; File='DollarAnalysisWindow.cs';
       Old='&& !_target.Weather && extreme)'; New='&& !_target.Weather)' },
    @{ Name='Spark card remains in basic'; File='DollarAnalysisWindow.cs';
       Old='_sparkCard.Visibility = extreme && !_target.Weather ? Visibility.Visible : Visibility.Collapsed;';
       New='_sparkCard.Visibility = !_target.Weather ? Visibility.Visible : Visibility.Collapsed;' },
    @{ Name='Spark mode switch keeps communication'; File='DollarAnalysisWindow.cs'; All=$true;
       Old='if (_refreshCancellation != null) _refreshCancellation.Cancel();'; New='// mode cancellation removed' },
    @{ Name='Spark failed request keeps scheduled timer'; File='DollarAnalysisWindow.cs';
       Old='&& !_sparkAutoPaused && _analysisMode.IsChecked == true;'; New='&& _analysisMode.IsChecked == true;' },
    @{ Name='Spark invalid excerpt ID coerced'; File='DollarSpark.cs';
       Old='double quoteId = e["quote_id"].D; var excerpts = QuoteExcerpts(article);'; New='double quoteId = 1; var excerpts = QuoteExcerpts(article);' },
    @{ Name='Spark validation reason hidden'; File='DollarSpark.cs';
       Old='"Spark 응답 오류 · " + error.Message'; New='"Spark 응답 오류"' },
    @{ Name='Spark invalid agents option restored'; File='DollarSpark.cs';
       Old=' -c features.multi_agent=false -c features.apps=false'; New=' -c features.multi_agent=false -c agents.enabled=false -c features.apps=false' },
    @{ Name='Spark CLI diagnostic discarded'; File='DollarSpark.cs';
       Old='FailureMessage(process.ExitCode, DiagnosticLines(errors.ToString()))'; New='FailureMessage(process.ExitCode, "")' },
    @{ Name='Spark CLI stdout becomes diagnostic'; File='DollarSpark.cs';
       Old='FailureMessage(process.ExitCode, DiagnosticLines(errors.ToString()))'; New='FailureMessage(process.ExitCode, text)' },
    @{ Name='Spark raw diagnostic leaks'; File='DollarSpark.cs';
       Old='"Spark 실행 실패 · CLI 종료 코드 " + exitCode.ToString(CultureInfo.InvariantCulture) + " · 확인되지 않은 실행 오류"'; New='standardError' },
    @{ Name='Spark first analysis blocked by throttle'; File='DollarAnalysisWindow.cs';
       Old='await RefreshCoreAsync(true);'; New='await RefreshCoreAsync(false);' },
    @{ Name='Spark successful login stays disconnected'; File='DollarAnalysisWindow.cs';
       Old='_sparkConnected = true;'; New='_sparkConnected = false;' },
    @{ Name='Spark expired login still analyzes'; File='DollarAnalysisWindow.cs';
       Old='if (!loggedIn)'; New='if (false)' },
    @{ Name='Spark timer fires before due'; File='DollarAnalysisWindow.cs';
       Old='_utcNow() < _nextSparkAt'; New='false' },
    @{ Name='Spark timer schedules from start'; File='DollarAnalysisWindow.cs';
       Old='_utcNow().AddSeconds(_sparkInterval)'; New='_lastAttempt.AddSeconds(_sparkInterval)' },
    @{ Name='Spark manual mode keeps timer running'; File='DollarAnalysisWindow.cs';
       Old='bool active = !_closed && _sparkConnected && _sparkInterval > 0 && !_sparkAutoPaused && _analysisMode.IsChecked == true;';
       New='bool active = !_closed && _sparkConnected && !_sparkAutoPaused && _analysisMode.IsChecked == true;' },
    @{ Name='Spark interval persistence lost'; File='Config.cs';
       Old='SparkRefreshIntervalSec = SparkInterval(j["sparkRefreshIntervalSec"].D);'; New='SparkRefreshIntervalSec = 300;' },
    @{ Name='prediction cards ignore compact scale'; File='WidgetWindow.cs';
       Old='const double cardScale = 0.7;'; New='const double cardScale = 1.0;' },
    @{ Name='prediction bars keep full buttons'; File='WidgetWindow.cs';
       Old='}, docked);'; New='}, false);' },
    @{ Name='prediction dots point sideways'; File='DollarAnalysisStyles.cs';
       Old="<StackPanel HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'>";
       New="<StackPanel Orientation='Horizontal' HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'>" },
    @{ Name='prediction price uses ECB anchor'; File='DollarAnalysisWindow.cs';
       Old='double anchor = PredictionTarget.Number(result.Quote);';
       New='double anchor = result.Rates.Count == 0 ? PredictionTarget.Number(result.Quote) : result.Rates.Last().Value;'; All=$true },
    @{ Name='FX history stops on filtered page'; File='PredictionData.cs';
       Old='if (json.Count < 60) break;'; New='if (values.Count < 60) break;' },
    @{ Name='prediction quote bank identity removed'; File='Sources.cs';
       Old='return def.Key + (def.Kind == SourceKind.Fx ? "|" + bank : "");'; New='return def.Key;' },
    @{ Name='sub quote decline painted red'; File='DollarAnalysisWindow.cs';
       Old='(ratio > 0 ? Palette.Up : Palette.Down) : Palette.TextDim;'; New='Palette.Up : Palette.TextDim;' },
    @{ Name='wrong instrument AI output accepted'; File='DollarSpark.cs';
       Old='if (!source.Target.Dollar && root["target_key"].S != source.Target.Key)'; New='if (false)' },
    @{ Name='prediction button opens dollar for every item'; File='WidgetWindow.cs';
       Old='open(def);'; New='open(new SymbolDef(SourceKind.Fx, "FX_USDKRW", "달러"));' },
    @{ Name='foreign stock hardcodes dollar currency'; File='Sources.cs';
       Old='return currency == "USD" ? "$" + price : price + " " + (currency ?? "현지통화");'; New='return "$" + price;' },
    @{ Name='history symbol mismatch accepted'; File='PredictionData.cs';
       Old='chart == null || chart.Attributes["symbol"].Value != code'; New='chart == null' },
    @{ Name='FX unfinished candle leaks'; File='PredictionData.cs';
       Old='p.Date < today && p.Date >= today.AddYears(-4)'; New='p.Date <= today.AddDays(1) && p.Date >= today.AddYears(-4)' },
    @{ Name='stock policy effect copied from dollar'; File='PredictionFactors.cs';
       Old='"discount-rate", hike ? -1 : 1'; New='"discount-rate", hike ? 1 : -1' },
    @{ Name='policy absolute change has opposite sign'; File='DollarSpark.cs';
       Old='(delta != 0 && Math.Sign(delta) != Math.Sign(sum))'; New='false' },
    @{ Name='latest shared quote ignored by analysis'; File='DollarAnalysisWindow.cs';
       Old='sharedQuote.ReceivedUtc > result.Quote.ReceivedUtc'; New='sharedQuote.ReceivedUtc < result.Quote.ReceivedUtc' },
    @{ Name='policy point unit misrepresented'; File='PredictionTarget.cs';
       Old='suffix = unit == "%" ? "%p" : unit;'; New='suffix = unit;' },
    @{ Name='forecast rows shift by period'; File='DollarAnalysisWindow.cs';
       Old='headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });';
       New='headingRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34 * (i + 1)) });' },
    @{ Name='disclosure fails to toggle'; File='DollarAnalysisStyles.cs';
       Old='RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay';
       New='RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay' },
    @{ Name='weak positive pressure concealed'; File='DollarAnalysisWindow.cs';
       Old='score.Value >= 0.05 ? "상승 쪽"';
       New='score.Value >= 5 ? "상승 쪽"' },
    @{ Name='login never starts'; File='DollarAnalysisWindow.cs';
       Old='await _login(loginToken);';
       New='await Task.FromResult(0);' },
    @{ Name='wrong style response accepted'; File='DollarSpark.cs';
       Old='if (root["style"].S != (source.Extreme ? "extreme" : "basic"))';
       New='if (false)' },
    @{ Name='extreme sends basic instructions'; File='DollarSpark.cs';
       Old='StyleInstructions(result.Extreme)';
       New='StyleInstructions(false)' },
    @{ Name='style switch mixes cached results'; File='DollarAnalysisWindow.cs';
       Old='_styleResults[extreme ? 1 : 0]';
       New='_styleResults[0]' },
    @{ Name='Spark invented citation accepted'; File='DollarSpark.cs';
       Old='Normalize(SourceText(article)).IndexOf(Normalize(quote), StringComparison.Ordinal) < 0';
       New='false' },
    @{ Name='Spark missing rebuttal accepted'; File='DollarSpark.cs';
       Old='Counter = RequiredText(n["counter"].S)';
       New='Counter = n["counter"].S' },
    @{ Name='Spark monthly uses daily'; File='DollarAnalysis.cs';
       Old='result.Spark.Periods.FirstOrDefault(p => p.Horizon == horizon)';
       New='result.Spark.Periods.FirstOrDefault(p => p.Horizon == 1)' },
    @{ Name='Spark forecast loses direction'; File='DollarAnalysis.cs';
       Old='if (score.Available) return (Math.Abs(score.Value) < 0.05 ? 0 : score.Value) / 100 * Math.Max(Math.Abs(pattern.LowerReturn), Math.Abs(pattern.UpperReturn));';
       New='if (score.Available) return pattern.MedianReturn;' },
    @{ Name='evidence ledger loses opposition'; File='DollarAnalysis.cs';
       Old='entry.Down = weight * negative * strength;';
       New='entry.Down = 0;' },
    @{ Name='evidence count uses collected articles'; File='DollarAnalysisWindow.cs';
       Old='_evidenceCounts[index].Text = (score.IsAi ? "인용 " : "검토 ") + score.Evidence.Count + "건";';
       New='_evidenceCounts[index].Text = "자료 " + result.News.Count + "건";' },
    @{ Name='Fed hike reversed'; File='DollarFactors.cs';
       Old='int direction = fed ? (cut ? -1 : 1) : (cut ? 1 : -1);';
       New='int direction = fed ? (cut ? 1 : -1) : (cut ? 1 : -1);' },
    @{ Name='Korean negation ignored'; File='DollarFactors.cs';
       Old='(?<![가-힣])안\s*(?:올|내|하|한|했|할|해)|';
       New='' },
    @{ Name='person signal treated as decision'; File='DollarFactors.cs';
       Old='(conditional ? 0.5 : 1)';
       New='(conditional ? 1 : 1)' },
    @{ Name='opposing factors inside article concealed'; File='DollarAnalysis.cs';
       Old='down += weight * negative * strength;';
       New='down += 0;' },
    @{ Name='details opened on main screen'; File='DollarAnalysisWindow.cs';
       Old='Header = "상세 보기 · 점수·뉴스·검증", IsExpanded = false';
       New='Header = "상세 보기 · 점수·뉴스·검증", IsExpanded = true' },
    @{ Name='missing evidence given an outlook'; File='DollarAnalysisWindow.cs';
       Old='if (!score.Available) return "과거 참고";';
       New='if (false) return "과거 참고";' },
    @{ Name='opposing evidence added as support'; File='DollarAnalysis.cs';
       Old='UpEvidence - DownEvidence + History';
       New='UpEvidence + DownEvidence + History' },
    @{ Name='monthly forecast uses daily return'; File='DollarAnalysis.cs';
       Old='sample.Returns.Add(rates[i + horizon].Value / rates[i].Value - 1);';
       New='sample.Returns.Add(rates[i + 1].Value / rates[i].Value - 1);' },
    @{ Name='future price in moving average'; File='DollarAnalysis.cs';
       Old='sum += rates[i].Value;';
       New='sum += rates[Math.Min(i + 1, rates.Count - 1)].Value;' },
    @{ Name='future history'; File='DollarAnalysis.cs';
       Old='if (date > today || date < today.AddYears(-3)) continue;';
       New='if (date < today.AddYears(-3)) continue;' },
    @{ Name='daily outcome used for month'; File='DollarAnalysis.cs';
       Old='Add(sample, Direction(rates[i + horizon].Value / rates[i].Value - 1, horizon));';
       New='Add(sample, Direction(rates[i + 1].Value / rates[i].Value - 1, horizon));' },
    @{ Name='stale and future news'; File='DollarAnalysis.cs';
       Old='if (time.UtcDateTime > nowUtc || time.UtcDateTime < nowUtc.AddHours(-24)) continue;';
       New='if (false) continue;' },
    @{ Name='opposing news concealed'; File='DollarAnalysis.cs';
       Old='if (lead != 0 && result.News.Any(n => n.Direction == -lead))';
       New='if (false)' },
    @{ Name='poor validation concealed'; File='DollarAnalysis.cs';
       Old='else if (p.ValidationHits <= p.BaselineHits)';
       New='else if (false)' },
    @{ Name='stale rates shown'; File='DollarAnalysis.cs';
       Old='(KoreaDate(nowUtc) - pattern.LatestDate).TotalDays <= 7';
       New='(KoreaDate(nowUtc) - pattern.LatestDate).TotalDays <= 700' },
    @{ Name='closed window communication kept'; File='DollarAnalysisWindow.cs';
       Old='_lifetime.Cancel();';
       New='// close cancellation removed' },
    # ── 전수 감사 (2026-09-10) ──────────────────────────────────────────
    @{ Name='article body with a bad JSON escape kills enrichment'; File='DollarNewsSources.cs'; Suite='dollar'; Old='if (decoded != null && decoded.Length > 150) return Plain(decoded);'; New='if (decoded.Length > 150) return Plain(decoded);' },
    @{ Name='holiday posted rate graded as today'; File='PredictionJournal.cs'; Suite='deep'; Old='if (q.TradedDate != DateTime.MinValue && q.TradedDate != DollarAnalysis.KoreaDate(now)) return false;'; New='if (false) return false;' },
    @{ Name='traded date never stamped on the won'; File='Sources.cs'; Suite='docs'; Old='TradedDate = DateOf(r["localTradedAt"].S),'; New='TradedDate = DateTime.MinValue,' },
    @{ Name='US stock due day follows the Korean calendar'; File='PredictionJournal.cs'; Suite='deep'; Old='int shiftHours = target.Def.Kind == SourceKind.WorldStock ? 0 : 9;'; New='int shiftHours = 9;' },
    @{ Name='stale shared quote masks a dead connection'; File='WidgetWindow.cs'; Suite='surge'; Old='if (age < 0 || age > 30) return own ?? new Quote();'; New='if (false) return own ?? new Quote();' },
    @{ Name='failed fetch counted as received'; File='WidgetWindow.cs'; Suite='surge'; Old='received = own != null && own.Ok;'; New='received = true;' },
    @{ Name='no quick retry after a failed fetch'; File='WidgetWindow.cs'; Suite='surge'; Old='int retryAfter = Math.Min(15 * Math.Max(1, failStreak), 120);'; New='int retryAfter = intervalSec;' },
    @{ Name='retry backoff never caps'; File='WidgetWindow.cs'; Suite='surge'; Old='int retryAfter = Math.Min(15 * Math.Max(1, failStreak), 120);'; New='int retryAfter = 15 * Math.Max(1, failStreak);' },
    @{ Name='own fetch echoes through the shared path'; File='WidgetWindow.cs'; Suite='surge'; Old='if (_selfFetching) return;'; New='if (false) return;' },
    @{ Name='deleted weather region resurrected'; File='WidgetWindow.cs'; Suite='surge'; Old='if (_cfg.Weathers.Count > 0 || _cfg.WeatherSeeded || _locationTried) return;'; New='if (_cfg.Weathers.Count > 0) return;' },
    @{ Name='weather seeding not remembered across restarts'; File='Config.cs'; Suite='json'; Old='if (Weathers.Count > 0 || WeatherSeeded) return;'; New='if (Weathers.Count > 0) return;' },
    @{ Name='stderr counted toward the response cap'; File='DollarSpark.cs'; Suite='dollar'; Old='process.OutputDataReceived += capture;'; New='process.OutputDataReceived += capture; process.ErrorDataReceived += capture;' },
    @{ Name='panel scale floor disagrees with the grip'; File='Config.cs'; Suite='json'; Old='if (v < MinPanelScale || v > MaxScale) return double.NaN;'; New='if (v < MinScale || v > MaxScale) return double.NaN;' },
    @{ Name='missing version treated as a pre-0.15 config'; File='Config.cs'; Suite='json'; Old='if (j["version"].Exists && VersionOf(FileVersion) > 0 && VersionOf(FileVersion) < 0.15 && j["scale"].Exists)'; New='if (VersionOf(FileVersion) < 0.15 && j["scale"].Exists)' },
    @{ Name='sub-won coin price rounded away'; File='Sources.cs'; Suite='json'; Old='return p >= 1000 ? 0 : p >= 1 ? 1 : p >= 0.01 ? 4 : 6;'; New='return p >= 1000 ? 0 : 1;' },
    @{ Name='older shared quote overwrites a newer one'; File='Sources.cs'; Suite='json'; Old='if (SharedQuotes.TryGetValue(key, out older) && older != null && older.ReceivedUtc > quote.ReceivedUtc) return;'; New='if (false) return;' },
    @{ Name='page furniture kept as article text'; File='DollarNewsSources.cs'; Suite='dollar'; Old='.Where(t => t.Length >= 25 && !Boilerplate(t)).ToArray();'; New='.Where(t => t.Length >= 25).ToArray();' },
    @{ Name='photo credit takes the reporting with it'; File='DollarNewsSources.cs'; Suite='json'; Old='@"\[[^\[\]]{0,160}?(?:재판매\s?및\s?DB\s?금지|무단\s?전재|자료\s?사진|제공)[^\[\]]{0,160}?\]", " ");'; New='@"^[\s\S]*$", " ");' },
    @{ Name='factor cache ignores the instrument'; File='PredictionFactors.cs'; Suite='dollar'; Old='if (n.FactorTargetKey == target.Key &&'; New='if (n.FactorTargetKey != null &&' },
    @{ Name='factor cache ignores a changed body'; File='DollarFactors.cs'; Suite='dollar'; Old='if (n.FactorTargetKey == null && (object)n.FactorTitle == (object)n.Title && (object)n.FactorContext == (object)n.Context) return n.Factors;'; New='if (n.FactorTargetKey == null && n.Factors.Count >= 0) return n.Factors;' },
    @{ Name='window zoom steps from the config file'; File='DollarAnalysisWindow.cs'; Suite='primary'; Old='double next = Config.ScaleOrDefault(step == 0 ? 1.0 : Math.Round(_textScale.ScaleX + step, 2));'; New='double next = Config.ScaleOrDefault(step == 0 ? 1.0 : Math.Round(_cfg.AnalysisScale + step, 2));' },
    @{ Name='render reads the real clock instead of the injected one'; File='DollarAnalysisWindow.cs'; Suite='dollar-layout'; Old='            DateTime now = _utcNow();
            if (result != null && result.Extreme != (_analysisMode.IsChecked == true)) return;'; New='            DateTime now = DateTime.UtcNow;
            if (result != null && result.Extreme != (_analysisMode.IsChecked == true)) return;' }
)
$caught = 0
if ($CaseFilter) { $cases = @($cases | Where-Object { $_.Name -match $CaseFilter }) }
if ($cases.Count -eq 0) { throw 'No sabotage cases selected' }
if ($ValidateOnly) {
    $bad = 0
    foreach ($case in $cases) {
        $source = [IO.File]::ReadAllText((Join-Path $root ('src\' + $case.File)))
        $n = [regex]::Matches($source, [regex]::Escape($case.Old)).Count
        if (($case.All -and $n -lt 1) -or (-not $case.All -and $n -ne 1)) { Write-Output ("STALE: " + $case.Name + " matches=" + $n); $bad++ }
    }
    Write-Output ("Anchors: " + $cases.Count + ", stale: " + $bad)
    exit $bad
}
foreach ($case in $cases) {
    $original = [IO.File]::ReadAllText((Join-Path $root ('src\' + $case.File)))
    $matchesCount = [regex]::Matches($original, [regex]::Escape($case.Old)).Count
    if (($case.All -and $matchesCount -lt 1) -or (-not $case.All -and $matchesCount -ne 1)) {
        throw ('Mutation anchor changed: ' + $case.Name)
    }
    $path = Join-Path $scratch ('src\' + $case.File)
    [IO.File]::WriteAllText($path, $original.Replace($case.Old, $case.New), [Text.UTF8Encoding]::new($true))
    try {
        $ErrorActionPreference = 'Continue' # Expected assertion failures write to native stderr.
        $suite = if ($case.Suite) { $case.Suite } else { "dollar" }
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'run.ps1') -Suite $suite -SourceRoot $scratch 2>&1
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($code -ne 1 -or ($output -join "`n") -notmatch 'FAIL: System.Exception:') {
            $output | ForEach-Object { Write-Output $_ }
            throw ('Mutation not caught by an assertion: ' + $case.Name)
        }
        $caught++
        Write-Output ('DETECTED: ' + $case.Name)
    }
    finally { $ErrorActionPreference = 'Stop'; [IO.File]::WriteAllText($path, $original, [Text.UTF8Encoding]::new($true)) }
}
Write-Output ("PASS: $caught/$($cases.Count) sabotage defects detected; production source unchanged")
