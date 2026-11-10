using System;
using System.Collections.Generic;
using UnityEngine;

namespace YzrLocal
{
    public static class ActivityRewards
    {
        private const int StaminaActivityId = 11853;
        private const int ConsumptionActivityId = 11854;
        private const int DailyRechargeActivityId = 11855;
        private const int TotalRechargeActivityId = 11856;

        private const string StaminaProgressKey =
            "YzrLocal.Activity.Progress.11853";
        private const string ConsumptionProgressKey =
            "YzrLocal.Activity.Progress.11854";
        private const string DailyRechargeProgressKey =
            "YzrLocal.Activity.Progress.11855";
        private const string TotalRechargeProgressKey =
            "YzrLocal.Activity.Progress.11856";
        private const string RechargeDayKey =
            "YzrLocal.Activity.RechargeDay";
        private const string TotalClaimMigrationKey =
            "YzrLocal.Activity.Claim.LegacyMigrated";
        private const string ClaimPrefix =
            "YzrLocal.Activity.Claim.";

        public static void AddStaminaUsed(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            EnsureRechargeDay();
            int value = SaturatingAdd(
                PlayerPrefs.GetInt(StaminaProgressKey, 0),
                amount);
            PlayerPrefs.SetInt(StaminaProgressKey, value);
            PlayerPrefs.Save();
            RefreshAll();
            Log("Stamina used progress=" + value);
        }

        public static void AddYuanBaoConsumption(int amount, CoinType type)
        {
            if (amount <= 0 || type != CoinType.YuanBao)
            {
                return;
            }

            EnsureRechargeDay();
            int value = SaturatingAdd(
                PlayerPrefs.GetInt(ConsumptionProgressKey, 0),
                amount);
            PlayerPrefs.SetInt(ConsumptionProgressKey, value);
            PlayerPrefs.Save();
            RefreshAll();
            Log("YuanBao consumption progress=" + value);
        }

        public static void AddRecharge(int rmb)
        {
            if (rmb <= 0)
            {
                return;
            }

            EnsureRechargeDay();
            int daily = SaturatingAdd(
                PlayerPrefs.GetInt(DailyRechargeProgressKey, 0),
                rmb);
            int total = SaturatingAdd(
                GetTotalRechargeProgress(),
                rmb);
            PlayerPrefs.SetInt(DailyRechargeProgressKey, daily);
            PlayerPrefs.SetInt(TotalRechargeProgressKey, total);
            PlayerPrefs.Save();
            RefreshAll();
            Log("Recharge progress daily=" + daily + " total=" + total);
        }

        public static void RefreshAll()
        {
            try
            {
                EnsureRechargeDay();
                ActivityController controller =
                    TempDataManager.Instance.theActivityController;
                if (controller == null ||
                    controller.activityInfoList == null)
                {
                    return;
                }

                ActivityDatas datas =
                    DataManager.Instance.ActivityMetaDatas;
                if (datas == null)
                {
                    return;
                }

                for (int i = 0;
                     i < controller.activityInfoList.Count;
                     i++)
                {
                    ActivityInfo info = controller.activityInfoList[i];
                    if (info == null)
                    {
                        continue;
                    }

                    int type = (int)info.type;
                    if (type == 5)
                    {
                        info.currencyAmount = (uint)GetProgress(
                            StaminaActivityId);
                        SetInfoStatuses(
                            info,
                            StaminaActivityId,
                            datas.staminaUsedDay.Count);
                    }
                    else if (type == 21)
                    {
                        RefreshConsumptionStates(
                            info,
                            datas.ConsumptionPeriodData.Count);
                    }
                    else if (type == 1)
                    {
                        info.currencyAmount =
                            (uint)GetTotalRechargeProgress();
                        SetInfoStatuses(
                            info,
                            TotalRechargeActivityId,
                            datas.accumulatedPayRewardList.Count);
                    }
                    else if (type == 6)
                    {
                        info.currencyAmount =
                            (uint)GetDailyRechargeProgress();
                        SetInfoStatuses(
                            info,
                            DailyRechargeActivityId,
                            datas.GetRechargeRewardByDay(
                                info.whichDay <= 0 ? 1 : info.whichDay)
                                .Count);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("RefreshAll ERR " + ex);
            }
        }

        public static void ClaimStaminaAndRefresh(
            ActivityStaminaUsedPanel panel,
            int activityId,
            int rewardId)
        {
            if (ClaimCore(activityId, rewardId) &&
                panel != null &&
                panel.mActivityInfo != null)
            {
                panel.InitPanel(panel.mActivityInfo);
            }
        }

        public static void ClaimPayAndRefresh(
            ActivityAccumulatedPayPanel panel,
            int activityId,
            int rewardId)
        {
            if (!ClaimCore(activityId, rewardId) || panel == null)
            {
                return;
            }

            ActivityInfo info = FindActivity(activityId);
            if (info != null)
            {
                panel.InitPanel(info);
            }
        }

        public static void ClaimConsumptionAndRefresh(
            ActivityConsumptionPeriodPanel panel,
            int zeroBasedIndex)
        {
            bool claimed = ClaimCore(
                ConsumptionActivityId,
                zeroBasedIndex + 1);
            if (!claimed || panel == null)
            {
                return;
            }

            ActivityInfo info = FindActivity(ConsumptionActivityId);
            if (info != null)
            {
                panel.InitPanel(info);
            }
        }

        private static bool ClaimCore(int activityId, int rewardIndex)
        {
            try
            {
                if (rewardIndex <= 0)
                {
                    return false;
                }
                if (IsClaimed(activityId, rewardIndex))
                {
                    ShowTip("\u8be5\u5956\u52b1\u5df2\u7ecf\u9886\u53d6");
                    return false;
                }

                ActivityInfo info = FindActivity(activityId);
                if (info == null)
                {
                    ShowTip("\u6d3b\u52a8\u672a\u627e\u5230");
                    return false;
                }
                if (!IsReached(info, rewardIndex))
                {
                    ShowTip("\u6d3b\u52a8\u6761\u4ef6\u5c1a\u672a\u8fbe\u6210");
                    return false;
                }

                MessageEventArgs message = new MessageEventArgs();
                if (!GrantReward(info, rewardIndex, message))
                {
                    ShowTip("\u5956\u52b1\u53d1\u653e\u5931\u8d25");
                    return false;
                }

                SetClaimed(activityId, rewardIndex);
                ActivityController controller =
                    TempDataManager.Instance.theActivityController;
                if (activityId != ConsumptionActivityId)
                {
                    controller.SetActivityRewardDone(
                        activityId,
                        rewardIndex);
                    controller.PopActivityRewardUI(
                        activityId,
                        rewardIndex);
                }
                RefreshAll();
                LocalProgression.SavePlayerData();
                ShowTip("\u5956\u52b1\u5df2\u9886\u53d6");
                return true;
            }
            catch (Exception ex)
            {
                Log("Claim ERR id=" + activityId +
                    " index=" + rewardIndex + " " + ex);
                ShowTip("\u9886\u53d6\u5f02\u5e38");
                return false;
            }
        }

        private static bool GrantReward(
            ActivityInfo info,
            int rewardIndex,
            MessageEventArgs message)
        {
            ActivityDatas datas = DataManager.Instance.ActivityMetaDatas;
            int type = (int)info.type;

            if (type == 5)
            {
                StaminaUsedDailyData data =
                    datas.staminaUsedDay[rewardIndex - 1];
                return GrantRewardLists(
                    data.reward_currencyType,
                    data.reward_currencyAmount,
                    data.reward_itemID,
                    data.reward_rule,
                    message);
            }
            if (type == 21)
            {
                ActivityPayRewardData data =
                    datas.ConsumptionPeriodData[rewardIndex - 1];
                return GrantRewardLists(
                    data.reward_currencyType,
                    data.reward_currencyAmount,
                    data.reward_itemID,
                    data.reward_rule,
                    message);
            }
            if (type == 1)
            {
                ActivityPayRewardData data =
                    datas.accumulatedPayRewardList[rewardIndex - 1];
                return GrantRewardLists(
                    data.reward_currencyType,
                    data.reward_currencyAmount,
                    data.reward_itemID,
                    data.reward_rule,
                    message);
            }
            if (type == 6)
            {
                List<ActivityPayRewardData> rewards =
                    datas.GetRechargeRewardByDay(
                        info.whichDay <= 0 ? 1 : info.whichDay);
                ActivityPayRewardData data = null;
                for (int i = 0; i < rewards.Count; i++)
                {
                    if (rewards[i].index == rewardIndex)
                    {
                        data = rewards[i];
                        break;
                    }
                }
                if (data == null)
                {
                    return false;
                }
                return GrantRewardLists(
                    data.reward_currencyType,
                    data.reward_currencyAmount,
                    data.reward_itemID,
                    data.reward_rule,
                    message);
            }
            return false;
        }

        private static bool GrantRewardLists(
            List<CoinType> currencyTypes,
            List<int> currencyAmounts,
            List<int> itemIds,
            List<int> rules,
            MessageEventArgs message)
        {
            PlayerDataStruct player = DataManager.Instance.playerDataStruct;
            if (player == null)
            {
                return false;
            }

            if (currencyTypes != null)
            {
                for (int i = 0; i < currencyTypes.Count; i++)
                {
                    int amount = currencyAmounts != null &&
                                 i < currencyAmounts.Count
                        ? currencyAmounts[i]
                        : 0;
                    if (amount > 0)
                    {
                        LocalProgression.AddCurrency(
                            player,
                            amount,
                            currencyTypes[i]);
                    }
                }
            }

            if (itemIds != null)
            {
                for (int i = 0; i < itemIds.Count; i++)
                {
                    int itemId = itemIds[i];
                    if (itemId <= 0)
                    {
                        continue;
                    }
                    int rule = rules != null && i < rules.Count
                        ? rules[i]
                        : 0;
                    int amount = LocalGameplay.GetRewardAmount(
                        itemId,
                        rule);
                    if (amount <= 0)
                    {
                        amount = 1;
                    }
                    if (!LocalGameplay.GrantSignInItemDirect(
                            itemId,
                            amount,
                            rule,
                            message))
                    {
                        Log("Grant item failed id=" + itemId +
                            " rule=" + rule + " amount=" + amount);
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool IsReached(ActivityInfo info, int rewardIndex)
        {
            ActivityDatas datas = DataManager.Instance.ActivityMetaDatas;
            int type = (int)info.type;

            if (type == 5)
            {
                return GetStaminaProgress() >=
                       datas.staminaUsedDay[rewardIndex - 1].unlockAmount;
            }
            if (type == 21)
            {
                return GetConsumptionProgress() >=
                       datas.ConsumptionPeriodData[rewardIndex - 1]
                           .unlock_currencyAmount;
            }
            if (type == 1)
            {
                return GetTotalRechargeProgress() >=
                       datas.accumulatedPayRewardList[rewardIndex - 1]
                           .unlock_currencyAmount;
            }
            if (type == 6)
            {
                List<ActivityPayRewardData> rewards =
                    datas.GetRechargeRewardByDay(
                        info.whichDay <= 0 ? 1 : info.whichDay);
                for (int i = 0; i < rewards.Count; i++)
                {
                    if (rewards[i].index == rewardIndex)
                    {
                        return GetDailyRechargeProgress() >=
                               rewards[i].unlock_currencyAmount;
                    }
                }
            }
            return false;
        }

        private static void SetInfoStatuses(
            ActivityInfo info,
            int activityId,
            int rewardCount)
        {
            List<int> statuses = info.finishStatus;
            if (statuses == null)
            {
                return;
            }
            while (statuses.Count < rewardCount)
            {
                statuses.Add((int)ActivityRewardStatus.OVERTIME);
            }

            int mask = GetClaimMask(activityId);
            for (int i = 1; i <= rewardCount; i++)
            {
                int status;
                if ((mask & (1 << (i - 1))) != 0)
                {
                    status = (int)ActivityRewardStatus.OBTAINED;
                }
                else if (IsReached(info, i))
                {
                    status = (int)ActivityRewardStatus.FINISHED;
                }
                else
                {
                    status = (int)ActivityRewardStatus.UNFINISHED;
                }
                statuses[i - 1] = status;
            }
        }

        private static void RefreshConsumptionStates(
            ActivityInfo info,
            int rewardCount)
        {
            TempDataManager manager = TempDataManager.Instance;
            manager.consumptionPeriod = GetConsumptionProgress();
            if (manager.consumptionPeriodStates == null)
            {
                manager.consumptionPeriodStates = new List<int>();
            }
            manager.consumptionPeriodStates.Clear();

            int mask = GetClaimMask(ConsumptionActivityId);
            for (int i = 1; i <= rewardCount; i++)
            {
                int status;
                if ((mask & (1 << (i - 1))) != 0)
                {
                    status = (int)ActivityRewardStatus.OBTAINED;
                }
                else if (IsReached(info, i))
                {
                    status = (int)ActivityRewardStatus.FINISHED;
                }
                else
                {
                    status = (int)ActivityRewardStatus.UNFINISHED;
                }
                manager.consumptionPeriodStates.Add(status);
            }
        }

        private static ActivityInfo FindActivity(int activityId)
        {
            ActivityController controller =
                TempDataManager.Instance.theActivityController;
            if (controller == null ||
                controller.activityInfoList == null)
            {
                return null;
            }
            for (int i = 0;
                 i < controller.activityInfoList.Count;
                 i++)
            {
                if (controller.activityInfoList[i].id == activityId)
                {
                    return controller.activityInfoList[i];
                }
            }
            return null;
        }

        private static int GetProgress(int activityId)
        {
            if (activityId == StaminaActivityId)
            {
                return GetStaminaProgress();
            }
            if (activityId == ConsumptionActivityId)
            {
                return GetConsumptionProgress();
            }
            if (activityId == DailyRechargeActivityId)
            {
                return GetDailyRechargeProgress();
            }
            if (activityId == TotalRechargeActivityId)
            {
                return GetTotalRechargeProgress();
            }
            return 0;
        }

        private static int GetStaminaProgress()
        {
            EnsureRechargeDay();
            return Math.Max(
                0,
                PlayerPrefs.GetInt(StaminaProgressKey, 0));
        }

        private static int GetConsumptionProgress()
        {
            EnsureRechargeDay();
            return Math.Max(
                0,
                PlayerPrefs.GetInt(ConsumptionProgressKey, 0));
        }

        private static int GetDailyRechargeProgress()
        {
            EnsureRechargeDay();
            return Math.Max(
                0,
                PlayerPrefs.GetInt(DailyRechargeProgressKey, 0));
        }

        private static int GetTotalRechargeProgress()
        {
            EnsureRechargeDay();
            int saved = Math.Max(
                0,
                PlayerPrefs.GetInt(TotalRechargeProgressKey, 0));
            if (saved > 0 || DataManager.Instance == null)
            {
                return saved;
            }

            PlayerDataStruct player = DataManager.Instance.playerDataStruct;
            if (player != null && player.VLevel >= 16)
            {
                PlayerPrefs.SetInt(TotalRechargeProgressKey, 5000);
                PlayerPrefs.Save();
                return 5000;
            }
            return saved;
        }

        private static void EnsureRechargeDay()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            if (PlayerPrefs.GetString(RechargeDayKey, string.Empty) ==
                today)
            {
                return;
            }

            PlayerPrefs.SetString(RechargeDayKey, today);
            PlayerPrefs.SetInt(StaminaProgressKey, 0);
            PlayerPrefs.SetInt(ConsumptionProgressKey, 0);
            PlayerPrefs.SetInt(DailyRechargeProgressKey, 0);
            PlayerPrefs.SetInt(TotalRechargeProgressKey, 0);
            PlayerPrefs.Save();
        }

        private static string ClaimKey(int activityId)
        {
            string suffix =
                activityId >= StaminaActivityId &&
                activityId <= TotalRechargeActivityId
                ? "." + DateTime.Now.ToString("yyyyMMdd")
                : string.Empty;
            return ClaimPrefix + activityId + suffix;
        }

        private static int GetClaimMask(int activityId)
        {
            if (activityId >= StaminaActivityId &&
                activityId <= TotalRechargeActivityId)
            {
                MigrateDailyClaims();
            }
            return PlayerPrefs.GetInt(ClaimKey(activityId), 0);
        }

        private static void MigrateDailyClaims()
        {
            if (PlayerPrefs.GetInt(TotalClaimMigrationKey, 0) != 0)
            {
                return;
            }

            string today = DateTime.Now.ToString("yyyyMMdd");
            for (int activityId = StaminaActivityId;
                 activityId <= TotalRechargeActivityId;
                 activityId++)
            {
                string datedKey = ClaimPrefix +
                                  activityId +
                                  "." + today;
                if (!PlayerPrefs.HasKey(datedKey))
                {
                    int legacy = PlayerPrefs.GetInt(
                        ClaimPrefix + activityId,
                        0);
                    if (legacy != 0)
                    {
                        PlayerPrefs.SetInt(datedKey, legacy);
                    }
                }
            }
            PlayerPrefs.SetInt(TotalClaimMigrationKey, 1);
            PlayerPrefs.Save();
        }

        private static bool IsClaimed(int activityId, int rewardIndex)
        {
            int mask = GetClaimMask(activityId);
            return (mask & (1 << (rewardIndex - 1))) != 0;
        }

        private static void SetClaimed(int activityId, int rewardIndex)
        {
            int mask = GetClaimMask(activityId);
            mask |= 1 << (rewardIndex - 1);
            PlayerPrefs.SetInt(ClaimKey(activityId), mask);
            PlayerPrefs.Save();
        }

        private static int SaturatingAdd(int left, int right)
        {
            long value = (long)left + right;
            if (value > int.MaxValue)
            {
                return int.MaxValue;
            }
            if (value < 0)
            {
                return 0;
            }
            return (int)value;
        }

        private static void ShowTip(string text)
        {
            try
            {
                GlobalSystemData.ShowTips(text);
            }
            catch
            {
            }
        }

        private static void Log(string text)
        {
            Debug.Log("[YzrLocal.Activity] " + text);
        }
    }
}
