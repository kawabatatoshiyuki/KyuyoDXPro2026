using System.Reflection;
using Microsoft.Data.Sqlite;

namespace PayCalcDXPro.Data.YearEnd;

/// <summary>
/// 年末調整計算が参照する法定マスタ（T_給与所得控除後／T_課税給与所得金額／T_基礎控除表／T_配偶者控除／
/// T_生保控除１/２／T_扶養障害者等／T_特定親族特別控除／T_速算表）を、アプリDBへシードする。
/// 給与計算の <c>RateSeed</c>/<c>TaxTableSeed</c> と同方式（CREATE TABLE IF NOT EXISTS ＋ 空なら投入）。
/// データは現行 PayCalc2026.db（令和7年）から自動生成した埋込SQL <c>YearEndMaster.sql</c>。
/// 移行（Migrator）で現行DB全体を取り込んだ場合は既に存在するため、本シードは何もしない。
/// </summary>
public sealed class YearEndMasterRepository
{
    private const string ResourceName = "PayCalcDXPro.Data.YearEnd.YearEndMaster.sql";

    private readonly SqliteConnection _conn;

    public YearEndMasterRepository(SqliteConnection conn) => _conn = conn;

    /// <summary>年末調整マスタが無ければ埋込SQLで投入する（既存データには触れない）。</summary>
    public void EnsureSeeded()
    {
        // 代表テーブルが既にあれば（＝移行済み or シード済み）何もしない。
        if (TableExists("T_給与所得控除後")) return;

        string sql = ReadEmbeddedSql();
        using var tx = _conn.BeginTransaction();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private bool TableExists(string name)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "select 1 from sqlite_master where type='table' and name=$n limit 1";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is not null;
    }

    private static string ReadEmbeddedSql()
    {
        var asm = Assembly.GetExecutingAssembly();
        using Stream? s = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"埋込リソースが見つかりません: {ResourceName}");
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }
}
