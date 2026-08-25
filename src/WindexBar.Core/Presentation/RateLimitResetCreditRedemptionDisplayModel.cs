using WindexBar.Core.Config;
using WindexBar.Core.Providers.Codex;

namespace WindexBar.Core.Presentation;

public sealed record RateLimitResetCreditRedemptionDisplayModel(
    string Title,
    string Message,
    bool IsSuccess);

public static class RateLimitResetCreditRedemptionDisplayModelFactory
{
    public static RateLimitResetCreditRedemptionDisplayModel Create(
        RateLimitResetCreditRedemptionAttempt attempt,
        string? language)
    {
        var isKorean = WindexBarConfig.NormalizeLanguage(language) == "ko";
        if (attempt.IsInProgress)
        {
            return new(
                isKorean ? "사용 중" : "Redemption in progress",
                isKorean ? "이미 리셋 크레딧 사용 요청을 처리하고 있어." : "A reset-credit redemption is already in progress.",
                IsSuccess: false);
        }

        if (!string.IsNullOrWhiteSpace(attempt.ErrorMessage))
        {
            return new(
                isKorean ? "리셋 크레딧 사용 실패" : "Reset-credit redemption failed",
                isKorean
                    ? $"요청 결과를 확인하지 못했어. 같은 요청으로 다시 시도하면 중복 소비되지 않아.\n\n{attempt.ErrorMessage}"
                    : $"The request result could not be confirmed. Retrying uses the same request and won't consume twice.\n\n{attempt.ErrorMessage}",
                IsSuccess: false);
        }

        return attempt.Outcome switch
        {
            RateLimitResetCreditRedemptionOutcome.Reset => new(
                isKorean ? "리셋 완료" : "Reset complete",
                isKorean ? "리셋 크레딧 1개를 사용해 적용 가능한 Codex 사용 제한을 초기화했어." : "One reset credit was used to reset the eligible Codex rate limit.",
                IsSuccess: true),
            RateLimitResetCreditRedemptionOutcome.AlreadyRedeemed => new(
                isKorean ? "이미 리셋 완료" : "Reset already completed",
                isKorean ? "같은 요청이 이미 성공적으로 처리됐어. 크레딧이 중복 소비되지는 않았어." : "The same request already completed successfully. No duplicate credit was consumed.",
                IsSuccess: true),
            RateLimitResetCreditRedemptionOutcome.NothingToReset => new(
                isKorean ? "초기화할 제한 없음" : "Nothing to reset",
                isKorean ? "현재 리셋 크레딧을 적용할 수 있는 Codex 사용 제한이 없어. 크레딧은 소비되지 않았어." : "There is no eligible Codex rate limit to reset. No credit was consumed.",
                IsSuccess: false),
            RateLimitResetCreditRedemptionOutcome.NoCredit => new(
                isKorean ? "사용 가능한 크레딧 없음" : "No reset credit available",
                isKorean ? "계정에 사용할 수 있는 리셋 크레딧이 없어. 최신 상태로 다시 불러왔어." : "The account has no available reset credits. The latest state was refreshed.",
                IsSuccess: false),
            _ => new(
                isKorean ? "알 수 없는 결과" : "Unknown result",
                isKorean ? "Codex가 알 수 없는 결과를 반환했어." : "Codex returned an unknown result.",
                IsSuccess: false)
        };
    }
}
