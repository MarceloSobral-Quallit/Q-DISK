Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports qdisk.Core.Models

Namespace Services
    ''' <summary>
    ''' Verificadores do token diario: para cada perfil e dia, PBKDF2-SHA256 do token daquele dia. O executavel leva so
    ''' estes verificadores (gerados por tools/utils/generate_token_verifiers.py a partir de config/qdisk.tokens.json),
    ''' nunca os internal_id: extrair o app nao entrega a regra para gerar tokens. A regra do site nao muda.
    ''' </summary>
    Public NotInheritable Class TokenVerifierSet
        Public Const Algorithm As String = "PBKDF2-SHA256"
        Public Const HashBytes As Integer = 16

        Public Property Iterations As Integer
        Public Property Salt As Byte() = Array.Empty(Of Byte)()
        Public Property FirstDate As Date
        Public Property LastDate As Date
        ''' <summary>Perfil -> (data yyyy-MM-dd -> hash hex).</summary>
        Public Property Verifiers As New Dictionary(Of UserProfile, Dictionary(Of String, String))()

        Public ReadOnly Property IsEmpty As Boolean
            Get
                Return Iterations <= 0 OrElse Salt.Length = 0 OrElse Verifiers.Count = 0
            End Get
        End Property

        Public Function Covers(day As Date) As Boolean
            Return Not IsEmpty AndAlso day.Date >= FirstDate AndAlso day.Date <= LastDate
        End Function

        ''' <summary>Hash do token para o perfil e o dia (mesma conta do gerador Python).</summary>
        Public Shared Function ComputeHash(token As String, profile As UserProfile, day As Date, salt As Byte(), iterations As Integer) As Byte()
            Dim dayText = day.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
            Dim saltText = Encoding.UTF8.GetBytes($"|{profile}|{dayText}")
            Dim fullSalt = New Byte(salt.Length + saltText.Length - 1) {}
            Buffer.BlockCopy(salt, 0, fullSalt, 0, salt.Length)
            Buffer.BlockCopy(saltText, 0, fullSalt, salt.Length, saltText.Length)
            Return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(token), fullSalt, iterations, HashAlgorithmName.SHA256, HashBytes)
        End Function

        ''' <summary>Perfil cujo verificador do dia corresponde ao token, ou None.</summary>
        Public Function Match(token As String, day As Date) As UserProfile
            Dim dayText = day.ToString("yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
            For Each pair In Verifiers
                Dim expectedHex As String = Nothing
                If Not pair.Value.TryGetValue(dayText, expectedHex) Then Continue For
                Dim expected = Convert.FromHexString(expectedHex)
                Dim actual = ComputeHash(token, pair.Key, day, Salt, Iterations)
                If CryptographicOperations.FixedTimeEquals(actual, expected) Then
                    Return pair.Key
                End If
            Next
            Return UserProfile.None
        End Function

        ''' <summary>Le o bloco "verifiers" do qdisk.tokens.app.json.</summary>
        Public Shared Function Parse(element As JsonElement) As TokenVerifierSet
            Dim result As New TokenVerifierSet()
            If element.ValueKind <> JsonValueKind.Object Then Return result
            Dim value As JsonElement
            If element.TryGetProperty("algorithm", value) AndAlso value.GetString() <> Algorithm Then Return result
            If element.TryGetProperty("iterations", value) AndAlso value.ValueKind = JsonValueKind.Number Then result.Iterations = value.GetInt32()
            If element.TryGetProperty("salt", value) Then result.Salt = Convert.FromBase64String(value.GetString())
            If element.TryGetProperty("from", value) Then result.FirstDate = Date.ParseExact(value.GetString(), "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
            If element.TryGetProperty("to", value) Then result.LastDate = Date.ParseExact(value.GetString(), "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture)
            If element.TryGetProperty("profiles", value) AndAlso value.ValueKind = JsonValueKind.Object Then
                For Each profileProperty In value.EnumerateObject()
                    Dim profile As UserProfile
                    If Not [Enum].TryParse(profileProperty.Name, ignoreCase:=True, result:=profile) OrElse profile = UserProfile.None Then Continue For
                    Dim days As New Dictionary(Of String, String)(StringComparer.Ordinal)
                    For Each dayProperty In profileProperty.Value.EnumerateObject()
                        days(dayProperty.Name) = dayProperty.Value.GetString()
                    Next
                    result.Verifiers(profile) = days
                Next
            End If
            Return result
        End Function
    End Class

    ''' <summary>
    ''' Limite de tentativas do token: depois de <see cref="FreeAttempts"/> erros seguidos, espera crescente (30 s, 1 min,
    ''' 2 min... ate 15 min). O estado fica num arquivo para valer tambem entre reinicios do app e na linha de comando.
    ''' </summary>
    Public NotInheritable Class TokenAttemptLimiter
        Public Const FreeAttempts As Integer = 3
        Private Shared ReadOnly FirstLock As TimeSpan = TimeSpan.FromSeconds(30)
        Private Shared ReadOnly MaxLock As TimeSpan = TimeSpan.FromMinutes(15)

        Private ReadOnly _statePath As String

        Public Sub New(statePath As String)
            _statePath = statePath
        End Sub

        Public Property Failures As Integer
        Public Property LockedUntilUtc As Date

        Public Function RemainingLock(nowUtc As Date) As TimeSpan
            Load()
            Dim remaining = LockedUntilUtc - nowUtc
            Return If(remaining > TimeSpan.Zero, remaining, TimeSpan.Zero)
        End Function

        Public Sub RegisterFailure(nowUtc As Date)
            Load()
            Failures += 1
            If Failures >= FreeAttempts Then
                Dim exponent = Math.Min(Failures - FreeAttempts, 10)
                Dim lockTime = TimeSpan.FromTicks(Math.Min(MaxLock.Ticks, FirstLock.Ticks * (1L << exponent)))
                LockedUntilUtc = nowUtc + lockTime
            End If
            Save()
        End Sub

        Public Sub RegisterSuccess()
            Failures = 0
            LockedUntilUtc = Date.MinValue
            Save()
        End Sub

        Private Sub Load()
            Failures = 0
            LockedUntilUtc = Date.MinValue
            Try
                If Not IO.File.Exists(_statePath) Then Return
                Dim parts = IO.File.ReadAllText(_statePath).Split("|"c)
                If parts.Length <> 2 Then Return
                Failures = Integer.Parse(parts(0), Globalization.CultureInfo.InvariantCulture)
                LockedUntilUtc = New Date(Long.Parse(parts(1), Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc)
            Catch
                Failures = 0
                LockedUntilUtc = Date.MinValue
            End Try
        End Sub

        Private Sub Save()
            Try
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(_statePath))
                IO.File.WriteAllText(_statePath, $"{Failures}|{LockedUntilUtc.Ticks}")
            Catch
            End Try
        End Sub
    End Class
End Namespace
