namespace PayCalcDXPro.Data.MyNumber;

/// <summary>マイナンバーの9スロット（0=本人,1=配偶者,2..8=扶養親族1..7）。現行 T_マイナンバー情報 の列に対応。</summary>
public static class MyNumberSlots
{
    /// <summary>表示用ラベル（種別）。配列インデックス＝スロット番号。</summary>
    public static readonly string[] 種別 =
    {
        "本人", "配偶者", "扶養親族１", "扶養親族２", "扶養親族３", "扶養親族４", "扶養親族５", "扶養親族６", "扶養親族７",
    };

    public const int Count = 9;
}

/// <summary>マイナンバー登録画面の1スロット：種別ラベル・氏名・登録済み(=入力可)・値（復号済み平文）。</summary>
public sealed record MyNumberSlot(int Index, string 種別, string 氏名, bool 登録済, string 値);

/// <summary>マイナンバー登録画面の表示モデル（1従業員分）。</summary>
public sealed record MyNumberView(int Number, string 従業員No, string 従業員氏名, IReadOnlyList<MyNumberSlot> スロット);
