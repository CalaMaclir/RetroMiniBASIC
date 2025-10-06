# Retro Mini BASIC interpreter

## 概要
Retro Mini BASIC interpreter は、古典的なBASIC文法をベースに、C#で実装された軽量インタプリタです。
教育用、レトロプログラミング体験用、また簡単なグラフィック実験にも使えるよう設計されています。

## 特徴
- 数値・文字列変数、1次元/2次元配列をサポート
- 四則演算・比較・論理演算に対応
- 制御構文：`IF/THEN`、`FOR/NEXT`、`DO/LOOP UNTIL`、`GOSUB/RETURN`、`ON GOTO/GOSUB`
- 入出力：`PRINT`、`INPUT`
- 数学関数・文字列関数・乱数・時間関数を提供
- グラフィック命令（点、線、円、矩形、色、保存）を備える
- レトロ風のカラーパレット（0〜15）が利用可能
- `POINT(x,y)` 関数で画素の状態も読み取れる

## 実行方法
1. Visual Studio または `dotnet run` で実行。  
2. 起動時に表示:
   ```
   Retro Mini BASIC interpreter Version 0.1
     by Cala Maclir 2025
   Ready.
   >
   ```
3. 行番号をつけて入力するとプログラムに保存されます。  
   ```
   > 10 PRINT "HELLO"
   > 20 END
   ```
   `RUN` で実行、`LIST` で確認、`NEW` で消去できます。  
4. 行番号なしで入力した場合は即時実行（ダイレクトモード）。  
5. `EXIT` で終了。

---

# リファレンスマニュアル

## 基本文法
- **代入**
  ```
  LET A = 10
  B$ = "ABC"      ' LETは省略可
  ```
- **PRINT**
  ```
  PRINT "X="; A
  PRINT A, B
  ```
- **INPUT**
  ```
  INPUT "値を入力:"; X
  INPUT NAME$
  ```
- **IF**
  ```
  IF A>0 THEN PRINT "POSITIVE"
  IF A=0 THEN 100
  ```
- **GOTO / GOSUB / RETURN**
  ```
  10 GOSUB 100
  20 PRINT "BACK": END
  100 PRINT "SUB": RETURN
  ```
- **FOR / NEXT**
  ```
  FOR I=1 TO 10 STEP 2
    PRINT I
  NEXT
  ```
- **DO / LOOP [UNTIL 条件]**
  ```
  DO
    INPUT A
  LOOP UNTIL A=0
  ```
- **DIM**
  ```
  DIM A(10)
  DIM B$(5,5)
  ```
- **ON GOTO / ON GOSUB**
  ```
  INPUT N
  ON N GOTO 100,200,300
  ```
- **STOP / END** … プログラム終了

## 数学関数
| 関数         | 内容                             |
|--------------|----------------------------------|
| `ABS(x)`     | 絶対値                           |
| `INT(x)`     | 小数切り捨て                     |
| `SQR(x)`     | 平方根                           |
| `SIN/COS/TAN(x)` | 三角関数 (ラジアン)          |
| `ATN(x)`     | 逆タンジェント                   |
| `LOG(x)`     | 自然対数 (x>0)                   |
| `EXP(x)`     | e^x                              |
| `PI`         | 円周率 (3.14159...)              |
| `RAD(x)`     | 度→ラジアン変換                  |
| `DEG(x)`     | ラジアン→度変換                  |
| `SGN(x)`     | 符号 (-1,0,1)                    |
| `MIN(a,b)`   | 最小値                           |
| `MAX(a,b)`   | 最大値                           |
| `CLAMP(x,a,b)` | 範囲に収める                   |

## 文字列関数
| 関数             | 内容                         |
|------------------|------------------------------|
| `STR$(x)`        | 数値→文字列                 |
| `VAL(s)`         | 文字列→数値                 |
| `LEN(s)`         | 文字数                      |
| `CHR$(n)`        | 文字コード→文字             |
| `ASC(s)`         | 文字→コード                 |
| `LEFT$(s,n)`     | 左からn文字                 |
| `RIGHT$(s,n)`    | 右からn文字                 |
| `MID$(s,p[,n])`  | 部分文字列                  |
| `SPC(n)`         | n個の空白                   |
| `TAB(n)`         | 桁揃え                       |

## 乱数・時間
| 関数                | 内容                           |
|---------------------|--------------------------------|
| `RND`               | 0.0〜1.0の乱数                |
| `RNDI(n)`           | 0〜n の整数乱数               |
| `RANDOMIZE [seed]`  | 乱数シード設定                |
| `TIMER`             | 実行開始からの経過秒数        |

## グラフィック命令
- **画面管理**
  ```
  SCREEN [w,h]   ' 初期化（既定640x480）
  CLS            ' 画面クリア
  FLUSH          ' バッファ更新
  SAVEIMAGE "a.png" ' PNG保存
  SLEEP ms       ' 一時停止
  ```
- **色**
  ```
  COLOR r,g,b
  COLOR palette
  COLORHSV h,s,v
  ```
  ※ paletteは0〜15のDOS風カラーパレット  
- **描画**
  ```
  PSET x,y
  LINE (x1,y1)-(x2,y2)[,color]
  LINE -(x2,y2)[,color]    ' shorthand
  CIRCLE x,y,r[,color]
  BOX x1,y1,x2,y2[,fill][,color]
  ```
- **判定**
  ```
  IF POINT(x,y) THEN PRINT "DOT"
  ```
## IF / THEN / ELSE

`IF` 文は `ELSE` にも対応しています。`THEN` の後も `ELSE` の後も、**行番号**または**文**を記述可能です。複数文は `:` で区切ります。

### 使用例

```basic
10 INPUT "点数"; S
20 IF S >= 60 THEN 100 ELSE 200
100 PRINT "合格": END
200 PRINT "不合格": END
```

- `S=100` → 合格  
- `S=10` → 不合格  
- `S=0` → 不合格  

### 文形式の ELSE
```basic
10 INPUT "値"; A
20 IF A>0 THEN PRINT "正": PRINT "POS" ELSE PRINT "負": PRINT "NEG"
30 END
```


---

# マンデルブロ集合（文字）

## 1. Hello World
```
10 FOR Y=-12 TO 12
20 FOR X=-39 TO 39
30 CA=X*0.0458
40 CB=Y*0.08333
50 A=CA
60 B=CB
70 FOR I=0 TO 15
80 T=A*A-B*B+CA
90 B=2*A*B+CB
100 A=T
110 IF (A*A+B*B)>4 THEN GOTO 200
120 NEXT I
130 PRINT " ";
140 GOTO 210
200 IF I>9 THEN I=I+7
205 PRINT CHR$(48+I);
210 NEXT X
220 PRINT
230 NEXT Y
```

## 2. マンデルブロ集合（グラフィック）
```
10 SCREEN 1280,960
20 CLS
30 MAX=100
40 FOR Y=0 TO 959
50 CI = -1.2 + Y*(2.4/959)
60 FOR X=0 TO 1279
70 CR = -2.2 + X*(3.2/1279)
80 XR=0: XI=0
90 I=0
100 FOR I=0 TO MAX
110 XT = XR*XR - XI*XI + CR
120 XI = 2*XR*XI + CI
130 XR = XT
140 IF XR*XR + XI*XI > 4 THEN GOTO 170
150 NEXT I
160 I=MAX
170 R = MIN(255, I*9)
180 G = MIN(255, I*7)
190 B = MIN(255, I*5)
200 COLOR R,G,B
210 PSET X,Y
220 NEXT X
230 IF Y MOD 8 = 0 THEN FLUSH
240 NEXT Y
250 FLUSH
```

## 3. ジュリア集合
```
10 SCREEN 1280,960
20 CLS
30 MAX=120
40 CR=-0.7
50 CI=0.27015
60 FOR Y=0 TO 959
70 Y0 = -1.2 + Y*(2.4/959)
80 FOR X=0 TO 1279
90 X0 = -2.2 + X*(3.2/1279)
100 XR = X0
110 XI = Y0
120 I=0
130 FOR I=0 TO MAX
140 XT = XR*XR - XI*XI + CR
150 XI = 2*XR*XI + CI
160 XR = XT
170 IF XR*XR + XI*XI > 4 THEN GOTO 200
180 NEXT I
190 I=MAX
200 R = MIN(255, I*5)
210 G = MIN(255, I*9)
220 B = MIN(255, I*3)
230 COLOR R,G,B
240 PSET X,Y
250 NEXT X
260 IF Y MOD 8 = 0 THEN FLUSH
270 NEXT Y
280 FLUSH
```

## 4. 3DHAT
```
10 SCREEN 640,480
20 CLS
25 COLOR 255,255,255
30 X1=2
40 DIM D(1,255)
45 FOR L=0 TO 255
46 D(0,L)=-1
47 D(1,L)=-1
48 NEXT
60 FOR Y=-180 TO 180 STEP 4
61 FOR X=-180 TO 180 STEP 4
80 R=PI/180*SQR(X*X+Y*Y)
90 Z=100*COS(R)-30*COS(3*R)
100 V=INT(116+X/2+(16-Y/2)/2)
110 W=INT((130-Y/2-Z)/2)
120 IF (V<0)+(V>255) THEN GOTO 160
130 IF D(0,V)=-1 THEN GOTO 500
140 IF W<=D(0,V) THEN GOTO 700
150 IF W>=D(1,V) THEN GOTO 800
155 GOTO 160
160 NEXT X
161 NEXT Y
165 FLUSH
170 END
500 IF V=0 THEN GOTO 600
510 IF D(0,V-1)=-1 THEN GOTO 600
520 IF D(0,V+1)=-1 THEN GOTO 600
530 D(0,V)=INT((D(0,V-1)+D(0,V+1))/2)
540 D(1,V)=INT((D(1,V-1)+D(1,V+1))/2)
550 GOSUB 900
560 GOTO 160
600 D(0,V)=W
605 D(1,V)=W
610 GOSUB 900
620 GOTO 160
700 GOSUB 900
705 D(0,V)=W
707 IF D(1,V)=-1 THEN D(1,V)=W
710 GOTO 160
800 GOSUB 900
805 D(1,V)=W
807 IF D(0,V)=-1 THEN D(0,V)=W
810 GOTO 160
900 X1=V*3.5+30
905 Y1=600-W*3.5
910 GOSUB 2100
920 RETURN
2100 REM DOT(X1,Y1)
2110 PSET X1/2*1.2+30,(-Y1/2+310)*1.2
2120 RETURN
```

## 5. 光輪
```
10 ' Retro Mini BASIC interpreter - 1920x1080 Hypercolor (no arrays, SQR版)
20 SCREEN 1920,1080
30 RANDOMIZE
40 CLS
50 W=1920: H=1080: CX=W/2: CY=H/2
60 T0=TIMER()
70 DO
80   T=TIMER()-T0
90   ' ---- 同心円グラデ ----
100  RMAX=SQR(CX*CX+CY*CY)
110  FOR R=0 TO RMAX STEP 6
120    HUE=(R*0.6+T*35) MOD 360
130    SAT=0.6+0.4*SIN(R*0.02+T*0.7)
140    VAL=0.35+0.65*(1-R/RMAX)
150    COLORHSV HUE,SAT,VAL
160    CIRCLE CX,CY,INT(R)
170  NEXT R
180  ' ---- 曲線トレイル ----
190  NPTS=1800: RBASE=MIN(W,H)*0.42
200  PH=T*22: TW=0.35*SIN(T*0.6)
210  PSET CX,CY
220  FOR K=0 TO NPTS
230    TH=(K/NPTS)*720+PH
240    R1=0.58+0.22*SIN(RAD(3*TH+T*40))
250    R2=0.25+0.18*SIN(RAD(7*TH-T*33))
260    R3=0.12+0.10*SIN(RAD(11*TH+T*21))
270    RR=(R1+R2+R3)*RBASE
280    A=RAD(TH+90*SIN(T*0.5))+TW*SIN(RAD(TH*0.9))
290    X=INT(CX+RR*COS(A)): Y=INT(CY+RR*SIN(A))
300    COLORHSV (TH*1.1+T*90) MOD 360,0.95,0.98
310    LINE -(X,Y)
320  NEXT K
330  ' ---- 光輪 ----
340  FOR I=0 TO 6
350    RR=RBASE*0.55+I*16
360    COLORHSV (T*60+I*38) MOD 360,0.7+0.3*SIN(T*0.8+I),0.85
370    CIRCLE CX,CY,INT(RR)
380  NEXT I
390  ' ---- 星粒（都度ランダム生成・配列なし）----
400  FOR S=0 TO 1200
410    X=INT(RND*W): Y=INT(RND*H)
420    IF RND<0.18 THEN COLORHSV (RND*360),0.8,0.95: PSET X,Y
430  NEXT S
440  FLUSH
450  SLEEP 16
460 LOOP
```
## 6. シェルピンスキーの三角形
```
10 SCREEN 800,800
20 CLS
30 DIM VX(2), VY(2)
40 VX(0)=400: VY(0)=0
50 VX(1)=0:   VY(1)=800
60 VX(2)=800: VY(2)=800
70 X=RNDI(799): Y=RNDI(799)
80 FOR I=1 TO 50000
90   K=RNDI(2)
100  X=(X+VX(K))/2
110  Y=(Y+VY(K))/2
120  COLOR 255,255,255
130  PSET X,Y
140  IF I MOD 2000=0 THEN FLUSH
150 NEXT I
160 FLUSH
```

## 7. バーンスレイのシダ
```
10 SCREEN 800,800
20 CLS
30 X=0: Y=0
40 FOR I=1 TO 50000
50 R=RND
60 IF R<0.01 THEN XX=0 : YY=0.16*Y
70 IF R>=0.01 AND R<0.86 THEN XX=0.85*X+0.04*Y : YY=-0.04*X+0.85*Y+1.6
80 IF R>=0.86 AND R<0.93 THEN XX=0.20*X-0.26*Y : YY=0.23*X+0.22*Y+1.6
90 IF R>=0.93 THEN XX=-0.15*X+0.28*Y : YY=0.26*X+0.24*Y+0.44
100 X=XX: Y=YY
110 COLOR 0,255,0
120 PSET 400+X*60, 800-Y*60
130 NEXT I
140 FLUSH
```

## 8. ペイントデモ
```
10 SCREEN 1920,1080
20 N=100        ' 1回あたりの四角形数
30 REP=500      ' 繰り返し回数
40 DIM CX(N-1), CY(N-1)   ' 各BOXの塗り座標（中心）を保持
50 FOR T=1 TO REP
60   CLS
70   COLOR 255,255,255
80   ' --- 100個の四角形（枠）を描画し、中心点を保存 ---
90   FOR I=0 TO N-1
100     X1=RNDI(1910): Y1=RNDI(1070)
110     W=20+RNDI(400): H=20+RNDI(300)
120     X2=X1+W: IF X2>1919 THEN X2=1919
130     Y2=Y1+H: IF Y2>1079 THEN Y2=1079
140     BOX X1,Y1,X2,Y2,0          ' 枠のみ（fill=0）
150     CX(I)=INT((X1+X2)/2)       ' 塗りの種点（中心）
160     CY(I)=INT((Y1+Y2)/2)
170   NEXT I
180   ' --- 各BOXの中をランダム色でPAINT ---
190   FOR I=0 TO N-1
200     COLOR RNDI(255),RNDI(255),RNDI(255)
210     PAINT CX(I),CY(I)
220     'FLUSH
230   NEXT I
235 FLUSH
240 NEXT T
250 END
```

# Retro Mini BASIC (IL) — VM 設計書

本書は **Retro Mini BASIC interpreter** の仮想マシン（VM）実装を理解・拡張するための技術ドキュメントです。  
対象は `VM.cs`（実行機）、`Compiler.cs`（IL 生成）、`GfxHost.cs`（描画）、`Program.cs`（REPL）に跨る挙動ですが、**VM 観点**での仕様を「readme 形式」で網羅します。

> 注: 一部の名称は実装上のフィールド/関数に合わせています。以降の説明で **「数値/文字列」「配列」「IL命令」「ジャンプテーブル」** という用語を共通で使用します。


---

## 1. 全体構成（アーキテクチャ）

```
┌──────────────┐    1) 行番号つきソース
│  Program.cs  │  ────────────────────────────┐
└──────┬───────┘                              │
       │ 2) コンパイル（構文解析・IL生成）   │
┌──────▼───────┐                              │
│ Compiler.cs  │  ──▶ CompiledProgram ───────┼─▶ 3) VM 実行
└──────┬───────┘                              │
       │（記号表/ジャンプ表を内包）          │
┌──────▼───────┐                              │
│    VM.cs     │  ◀───────────────────────────┘
└───┬─────┬────┘
    │     │
    │     └────────► GfxHost.cs（描画/UI）
    │
    └──────────────► Console I/O（INPUT/PRINT）
```

- `Compiler.cs` は **行番号付きプログラム**を **IL 命令列（Op[]）** に変換します（同時に **記号表**・**PC⇔行番号マップ**・**ON GOTO/GOSUB 用ジャンプテーブル**を構築）。
- `VM.cs` は **Op[]** を 0 から順に解釈し、**スタック実行**・**レジスタ/配列領域**・**制御スタック（GOSUB など）** を用いてプログラムを進行させます。
- グラフィックス命令は VM から `GfxHost` に委譲され、**ダブルバッファ**で UI スレッドに反映されます。


---

## 2. データモデル

### 2.1 値の型と真偽
- **数値**: `double`（数式/関数/比較演算に使用）
- **文字列**: `.NET string`
- **真偽の評価**: **`0` を偽、**`0 以外` を真**として扱います（比較/論理の結果も 0 or 非 0）。

### 2.2 変数レイアウト（スカラ）
- スカラは **数値配列** と **文字列配列**を別々に持つ **レジスタファイル**に格納されます。
- `Compiler` の `Symtab.GetScalarSlot(name)` は **LSB に型ビット**を埋め込むスロット番号を返します：
  - `slot & 1 == 0` → **数値**（`double[]` の index は `slot >> 1`）
  - `slot & 1 == 1` → **文字列**（`string[]` の index は `slot >> 1`）

### 2.3 配列レイアウト（1D/2D）
- 配列も **数値配列** と **文字列配列**に分かれて保持され、**最大 2 次元**。
- `DIM A(n)` / `DIM B$(r,c)` で確保。インデックスは **0 起点**。
- `Compiler` は `DIM_ARR (a:slot, b:次元数)` を発行し、**直前にプッシュされたサイズ（1 or 2 個）**を VM が消費して確保します。
- `LOAD_ARR/STORE_ARR` は **b=1 or 2（次元数）** に従って、VM がインデックスをスタックから読み、配列へアクセスします。

> 例: `DIM A(10)` → `PUSH 10` → `DIM_ARR a=A, b=1`  
> 例: `A(I,J)=X` → `… I … J … X` → `STORE_ARR a=A, b=2`


---

## 3. 実行モデル（スタックマシン）

### 3.1 スタック
- VM は **評価スタック**を持ち、式の評価や関数引数などはスタックで受け渡しします。
- 単項/二項演算はスタック上の値を **ポップ→演算→プッシュ** で進みます。

### 3.2 レジスタファイル
- スカラ数値: `numRegs[]`、スカラ文字列: `strRegs[]`
- 配列: `numArrs[]`, `strArrs[]` （内部は `double[]` または `string[]` の 1D/2D ラッパ）

### 3.3 制御スタック
- `GOSUB` は **復帰アドレス**を制御スタックに積み、`RETSUB` でポップして復帰します。
- `FOR/NEXT` は **ループ変数スロット/終端値/ステップ/比較方向/ボディPC** などを VM 側で保持します（詳細は 5.1）。


---

## 4. IL 命令セット（OpCode）

> `Compiler.cs` で発行される命令を VM が解釈します。`Op` は `Code`（オペコード）と `A/B/D/S` のオペランドスロットを持ちます。

### 4.1 スタック/変数/配列
| 命令 | 役割 |
|---|---|
| `PUSH_NUM (D)` | 数値リテラルをスタックに積む |
| `PUSH_STR (S)` | 文字列リテラルをスタックに積む |
| `LOAD (A=slot)` | スカラ変数をロードしてスタックへ |
| `STORE (A=slot)` | スタックトップをスカラ変数へ代入 |
| `LOAD_ARR (A=slot, B=dim)` | A の配列から **B 次元のインデックス**をポップ → 要素をプッシュ |
| `STORE_ARR (A=slot, B=dim)` | 値と **B 次元のインデックス**をポップ → A の配列へ書き込み |
| `DIM_ARR (A=slot, B=dim)` | スタックからサイズ `dim` 個を読み、配列を確保 |

### 4.2 算術/比較/論理
| 命令 | 役割 |
|---|---|
| `ADD/SUB/MUL/DIV/MOD/POW/NEG` | 通常の算術 |
| `CEQ/CNE/CLT/CLE/CGT/CGE` | 比較（結果は 0/非0） |
| `NOT/AND/OR` | 論理演算（0/非0 で扱う） |

### 4.3 出力/入力
| 命令 | 役割 |
|---|---|
| `PRINT` | スタックトップを即座に出力バッファへ |
| `PRINT_SPC` | `PRINT` 項目間の区切り（半角空白） |
| `PRINT_SUPPRESS_NL` | 行末の改行抑制 |
| `PRINT_NL` | 改行出力 |
| `CALLFN (A=fnId, B=argc)` | 関数/組み込みコマンド呼び出し（`INPUT` は特殊：`B` にスロットが入る） |

### 4.4 分岐/サブルーチン/テーブルジャンプ
| 命令 | 役割 |
|---|---|
| `JMP (A=pc)` | 無条件ジャンプ |
| `JZ (A=pc)` | スタックトップが **偽（0）**なら `A` へ |
| `GOSUB (A=pc)` | 制御スタックに復帰アドレスを積んで `A` へ |
| `RETSUB` | 制御スタックから復帰 |
| `ON_GOTO (A=tableIndex)` | スタックトップの値（1 起点）でテーブル分岐（`GOTO`） |
| `ON_GOSUB (A=tableIndex)` | 同上（`GOSUB`） |

### 4.5 FOR/NEXT
| 命令 | 役割 |
|---|---|
| `FOR_INIT (A=varSlot)` | スタックから `end`, `step` を読み、`var = var`（事前に `STORE` 済み） |
| `FOR_CHECK (A=varSlot, B=bodyPc)` | `var` が範囲内でなければ **ループ終端**へ、範囲内なら `B`（ボディ）へ |
| `FOR_INCR (A=varSlot | -1)` | `var += step`（`NEXT` に変数が省略された場合は最内ループを対象） |


---

## 5. 制御構文の実行時挙動

### 5.1 FOR / NEXT
- 構文: `FOR i = start TO end [STEP step] … NEXT [i]`
- コンパイル系列（概念図）:
  1) `start` を評価 → `STORE i`
  2) `end` → `step(省略時 1)` をプッシュ → `FOR_INIT i`
  3) `FOR_CHECK i, bodyPc`
  4) **ボディ**
  5) `FOR_INCR i` → `FOR_CHECK i, bodyPc` へ戻る or ループを抜ける

- 比較方向は `step` の符号で自動的に決まり、`i` が終端を超えた時点で終了します。

### 5.2 IF / THEN [/ ELSE]
- 条件は **0/非0** で評価。
- パターン:
  - `IF cond THEN <行番号>` → `JZ skip ; JMP line`
  - `IF cond THEN 文…` → `JZ afterThen ; 文… ; afterThen:`
  - `IF cond THEN <行> ELSE <行>` → `JZ else ; JMP then ; else: JMP elseLine`
  - `IF cond THEN 文… ELSE 文…` → `JZ else ; 文… ; JMP end ; else: 文… ; end:`
- `:` による **複文** は THEN/ELSE の両方で許可。

### 5.3 DO / LOOP [UNTIL cond]
- `DO` で **ループ先頭 PC** を積み、`LOOP UNTIL cond` で `cond` を評価し、**偽なら先頭へ JMP**、**真なら脱出**。
- `LOOP`（UNTIL なし）は無条件ループ（`JMP` のみ）。

### 5.4 ON GOTO / ON GOSUB
- `ON n GOTO l1,l2,...`：`n`（1 起点）が `k` なら `lk` へジャンプ。
- 範囲外（<1 or >N）は **落下（何もしない）**。`GOSUB` 版は復帰アドレスを積む。


---

## 6. 関数とビルトイン（CALLFN）

### 6.1 関数 ID（抜粋）
- 数値/文字列: `ABS, INT, VAL, STR$, LEN, CHR$, ASC, LEFT$, RIGHT$, MID$`
- 数学/時間/乱数: `SIN, COS, TAN, SQR, ATN, LOG, EXP, PI, RAD, DEG, SGN, MIN, MAX, CLAMP, RNDI, TIMER, RANDOMIZE`
- 出力補助: `SPC, TAB`
- 入力: `INPUT`（**特殊**：`CALLFN(A=INPUT, B=slot)` でスロットに代入）
- グラフィック（200 台）: `SCREEN, CLS, COLOR, PSET, LINE, CIRCLE, BOX, FLUSH, COLORHSV, SAVEIMAGE, SLEEP, POINT`

### 6.2 グラフィック命令の引数
- `LINE` は **3 形態**をサポート：
  1) `LINE (x1,y1)-(x2,y2)[,color]`
  2) `LINE -(x2,y2)[,color]`（**省略/ショートハンド**: 直前のペン座標→(x2,y2)）
  3) `LINE x1,y1,x2,y2[,color]`（保険のフラット形式）
- `Compiler` は **省略フラグ**を `argc` の **bit30** に埋め込み、VM はそれを見て `GfxHost.SetPen` を使った解釈を行います。

### 6.3 INPUT（特殊動作）
- `INPUT "prompt"; X` のように **文字列リテラルプロンプト**を先出し可能。
- VM は `CALLFN(INPUT, slot)` を受けて **コンソール読み取り**→ **型に応じて** `numRegs` または `strRegs` に代入します。


---

## 7. グラフィックス実装（GfxHost）

- **ダブルバッファ**: `backBuffer`（描画/読取）と `frontBuffer`（表示専用）を保持。
- **FLUSH**: `backBuffer.Clone()` を UI スレッドに渡して `PictureBox.Image` 差し替え。
- **スレッド分離**: UI は STA スレッド上で `Application.Run`、ワーカー側は `BlockingCollection<Action>` 経由で UI 操作要求を送達。
- **色**: `COLOR r,g,b` と `COLORHSV h,s,v` をサポート（HSV→RGB は VM 側/Host 側いずれかで変換）。
- **ペン座標**: `PSET/LINE/CIRCLE/BOX` 実行後に **終点をペン位置に更新**（ショートハンド `LINE -(x2,y2)` に利用）。
- **POINT(x,y)**: `backBuffer.GetPixel` を参照して **非黒（R|G|B ≠ 0）か**を真偽で返す。

> 画像保存は `SAVEIMAGE "file.png"`（PNG）。


---

## 8. エラー処理とデバッグ

- `Program.cs` は例外を受け取り、`vm.LastLine` と `PcToLine` で **行番号**を添えて表示します（`UNDEF'D STATEMENT` の類は `?` 表示）。
- 構文/解決エラー（コンパイル時）は `"(at compile)"` 付きで REPL 表示。
- 実行時エラーは `"(program, line N)"` の形式で表示されます。

**代表的なエラー**
- `SYNTAX ERROR at line L, col C: ...`
- `UNDEF'D STATEMENT (line X)`（行番号解決不可）
- `BAD JUMP TARGET`（ジャンプ先異常）

> 既定では **Ctrl+C ブレーク**は未実装です。必要なら VM の `Run()` ループに `Console.KeyAvailable` チェックを入れて `BreakException` を投げる拡張が容易です。


---

## 9. パフォーマンスとスレッド考慮

- `GfxHost.Flush` の頻度が高すぎると UI スレッド切替コストが増大します。描画ループでは **フレーム単位でまとめて FLUSH** するのが推奨です。
- `POINT` を大量に呼ぶ場合は `GetPixel` のロック/コピーが支配的になるため、**走査はアプリ側でまとめる**か **ラスタに直接アクセスする API** の導入を検討してください。


---

## 10. 拡張ポイント（実装ガイド）

- **ELSE 対応の IF**: `CompileIf()` の JZ/JMP を適切に並べる（本実装済）。
- **WHILE/WEND**: `cond`→`JZ end`→`body`→`JMP cond`→`end` の IL 生成。
- **INKEY$ / MOUSE**: `GfxHost` に入力状態を保持し、`CALLFN` から取得。
- **テキスト描画 (`GTEXT`)**: `GfxHost.DrawString` かビットマップフォント描画で最小実装。
- **SAVE/LOAD (プログラム)**: `Program.cs` に外部ファイル I/O コマンドを追加。

> VM 側は **命令を増やすよりも `CALLFN` を肥大化させる方が拡張容易**です（新しいビルトインを `FnId` に追加→VM の `CALLFN` スイッチで分岐）。


---

## 11. 互換性ノート

- 配列は **0 起点**である点に注意（古典 BASIC では 0/1 起点が混在）。
- `LINE ..., PSET, ...` の **旧式修飾は削除**（現行は `, color` のみ許容）。
- `RND/PI/TIMER` は **括弧省略の 0 引数**を許可。その他は基本的に `()` が必要。


---

## 12. 最小テスト（VM 動作確認スニペット）

```basic
10 ' IF/ELSE の回帰テスト
20 A=100: IF A>=60 THEN X=1 ELSE X=2: PRINT "T1=";X   ' 期待: T1=1
30 A=10:  IF A>=60 THEN X=1 ELSE X=2: PRINT "T2=";X   ' 期待: T2=2
40 A=0:   IF A>=60 THEN X=1 ELSE X=2: PRINT "T3=";X   ' 期待: T3=2
50 END
```

```basic
10 ' FOR/NEXT と配列
20 DIM N(4)
30 FOR I=0 TO 4
40   N(I)=I*I
50 NEXT
60 FOR I=0 TO 4: PRINT N(I);: NEXT: PRINT
70 END
```

```basic
10 ' グラフィック（円運動）
20 SCREEN 640,480: CLS
30 DO
40   H=(TIMER*60) MOD 360
50   COLORHSV H,1,1
60   X=320+150*COS(RAD(H)): Y=240+150*SIN(RAD(H))
70   PSET X,Y
80   FLUSH
90 LOOP
```


---

## 付録: Op 構造体と命令の運用
- `Op` は `Code`（`OpCode` 列挙）、`A/B/D/S` の 4 種のオペランドを持つ構造体。
- 数値/文字列のプッシュは `Op.Num(v)` / `Op.Str(s)` のファクトリ。
- `PcToLine[]` により **PC→行番号**、`LineToPc` により **行番号→PC** の解決を行います（コンパイル後の再解決もあり）。

---

以上。VM の内部仕様に沿って拡張・最適化・デバッグを行う際のリファレンスとしてご利用ください。
