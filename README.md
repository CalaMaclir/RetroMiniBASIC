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
   Type EXIT to quit. Lines beginning with a number are stored.
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
10 SCREEN 640,480
20 CLS
30 MAX=100
40 FOR Y=0 TO 479
50 CI = -1.2 + Y*(2.4/479)
60 FOR X=0 TO 639
70 CR = -2.2 + X*(3.2/639)
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
10 SCREEN 640,480
20 CLS
30 MAX=120
40 CR=-0.7
50 CI=0.27015
60 FOR Y=0 TO 479
70 Y0 = -1.2 + Y*(2.4/479)
80 FOR X=0 TO 639
90 X0 = -2.2 + X*(3.2/639)
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



# Retro Mini BASIC (IL) — VM 設計書

本書は **Retro Mini BASIC interpreter** の仮想マシン（VM）実装を理解・拡張するための技術ドキュメントです。  
対象は `VM.cs`（実行機）、`Compiler.cs`（IL 生成）、`GfxHost.cs`（描画）、`Program.cs`（REPL）に跨る挙動ですが、**VM 観点**での仕様を「readme 形式」で網羅します。

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

- `Compiler.cs` は **行番号付きプログラム**を **IL 命令列（Op[]）** に変換します。
- `VM.cs` は **Op[]** を 0 から順に解釈し、**スタック実行**・**変数/配列領域**・**制御スタック**を用いてプログラムを進行。
- グラフィックス命令は VM から `GfxHost` に委譲されます。

---

## 2. データモデル

- 数値は `double`、文字列は `string`。
- 真偽判定は 0=偽 / 0以外=真。  
- スカラ変数は `num[]`, `str[]` に格納。  
- 配列は 1D/2D をサポートし、DIM で確保。  

---

## 3. 実行モデル

- スタックマシン方式。  
- 算術/比較/論理はスタックの値をポップ→演算→プッシュ。  
- GOSUB/RETURN, FOR/NEXT などは制御スタックで管理。  

---

## 4. IL 命令セット (OpCode)

| 区分 | 命令 | 内容 |
|------|------|------|
| スタック | PUSH_NUM, PUSH_STR | 定数を積む |
|        | LOAD, STORE | スカラ変数読み書き |
| 配列   | DIM_ARR | 配列確保 (1D/2D) |
|        | LOAD_ARR, STORE_ARR | 配列要素の読み書き |
| 算術   | ADD, SUB, MUL, DIV, POW, NEG, MOD | 四則演算・累乗・符号反転・剰余 |
| 論理/比較 | CEQ, CNE, CLT, CLE, CGT, CGE, NOT, AND, OR | 比較・論理演算 |
| 制御   | JMP, JZ | 無条件/条件分岐 |
|        | FOR_INIT, FOR_CHECK, FOR_INCR | FOR/NEXT |
|        | GOSUB, RETSUB | サブルーチン呼出し |
|        | ON_GOTO, ON_GOSUB | ON GOTO/GOSUB |
| 関数呼出 | CALLFN | FnId に基づく関数/命令呼出 |
| 入出力 | PRINT, PRINT_SPC, PRINT_SUPPRESS_NL, PRINT_NL | PRINT 系 |
|        | INPUT | 入力 (FnId.INPUT) |
| グラフィック | CALLFN(FnId.SCREEN, GCOLOR 等) | グラフィック処理 |
| FORTH互換 | DUP, DROP, SWAP, OVER, ROT | スタック操作 |
|        | PRINT_STACK, PRINT_CR, EMIT_CHAR | 出力関連 |
|        | BAND, BOR, BXOR | ビット演算 |
| 終了   | HALT | 実行停止 |

---

## 5. 備考

- 本 README.md は BASIC モードに特化。FORTH は別途仕様。  
- IL 命令セットは BASIC/FORTH 共通の VM 上で実行される。

