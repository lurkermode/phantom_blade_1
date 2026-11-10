using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace YzrLocal
{
    public static class NewServerActivityRewards
    {
        private const int RewardObtainedEvent = 400;
        private const int ServerFirstChargeVipLimit = 5;
        private const int ObtainedStatus = 2;
        private const int FinishedStatus = 1;
        private const int UnfinishedStatus = 0;

        private const string ServerFirstNormalClaimPrefix =
            "YzrLocal.NewServer.Claim.ServerFirst.Normal.";
        private const string ServerFirstVipClaimPrefix =
            "YzrLocal.NewServer.Claim.ServerFirst.Vip.";
        private const string VipPrizeClaimPrefix =
            "YzrLocal.NewServer.Claim.VipPrize.";
        private const string WindmillIndexPrefix =
            "YzrLocal.NewServer.Windmill.Index.";

        public static int GetDailyRechargeAmount()
        {
            return ActivityRewards.GetDailyRechargeAmount();
        }

        public static DateTime GetActivityBeginTime()
        {
            ActivityRewards.EnsureDailyState();
            return DateTime.Today;
        }

        public static DateTime GetActivityEndTime()
        {
            ActivityRewards.EnsureDailyState();
            return DateTime.Today.AddDays(1).AddSeconds(-1);
        }

        public static ActivityRewardStatus GetServerFirstChargeStatus(
            int pid,
            bool isVip)
        {
            ActivityRewards.EnsureDailyState();
            string prefix = isVip
                ? ServerFirstVipClaimPrefix
                : ServerFirstNormalClaimPrefix;
            if (IsClaimed(prefix, pid))
            {
                return ActivityRewardStatus.OBTAINED;
            }

            ServerFirstChargeActivityData data =
                GetServerFirstChargeData(pid);
            if (data == null ||
                GetDailyRechargeAmount() < data.chargeCount)
            {
                return ActivityRewardStatus.UNFINISHED;
            }

            if (isVip && GetVipLevel() < ServerFirstChargeVipLimit)
            {
                return ActivityRewardStatus.UNFINISHED;
            }
            return ActivityRewardStatus.FINISHED;
        }

        public static void ClaimServerFirstCharge(
            ServerFirstChargeActivityContent content,
            int pid,
            bool isVip)
        {
            try
            {
                ActivityRewards.EnsureDailyState();
                ActivityRewardStatus status =
                    GetServerFirstChargeStatus(pid, isVip);
                if (status == ActivityRewardStatus.OBTAINED)
                {
                    ShowTip("该奖励今日已经领取");
                    return;
                }
                if (status != ActivityRewardStatus.FINISHED)
                {
                    ShowTip(isVip
                        ? "充值金额或VIP等级尚未达到条件"
                        : "今日累计充值金额尚未达到条件");
                    return;
                }

                ServerFirstChargeActivityData data =
                    GetServerFirstChargeData(pid);
                if (data == null)
                {
                    ShowTip("奖励配置不存在");
                    return;
                }

                MessageEventArgs message = new MessageEventArgs();
                if (!GrantItems(
                        isVip ? data.vipItemIDList : data.itemIDList,
                        isVip ? data.vipItemRuleList : data.itemRuleList,
                        message))
                {
                    ShowTip("奖励发放失败");
                    return;
                }

                string prefix = isVip
                    ? ServerFirstVipClaimPrefix
                    : ServerFirstNormalClaimPrefix;
                SetClaimed(prefix, pid);
                message.AddMessage(
                    "ServerFirstChargeActivityRewardObtained",
                    string.Empty);
                YuXueUIUtils.OpenNewItemWindowFromGiftMessage(message);
                EventManager.Instance.PostEvent(
                    (EventDefine)RewardObtainedEvent,
                    message);
                LocalProgression.SavePlayerData();
                ShowTip("全服首充奖励已领取");
                Log("Claim server-first pid=" + pid + " vip=" + isVip);
            }
            catch (Exception ex)
            {
                ShowTip("领取全服首充奖励时出现异常");
                Log("Claim server-first ERR " + ex);
            }
        }

        public static ActivityRewardStatus GetVipPrizeStatus(int pid)
        {
            ActivityRewards.EnsureDailyState();
            if (IsClaimed(VipPrizeClaimPrefix, pid))
            {
                return ActivityRewardStatus.OBTAINED;
            }

            VipPrizeActivityData data = GetVipPrizeData(pid);
            if (data == null || GetVipLevel() < data.pid)
            {
                return ActivityRewardStatus.UNFINISHED;
            }
            return ActivityRewardStatus.FINISHED;
        }

        public static void BuyVipPrize(
            VIPPrizeActivityContent content,
            int pid)
        {
            try
            {
                ActivityRewards.EnsureDailyState();
                ActivityRewardStatus status = GetVipPrizeStatus(pid);
                if (status == ActivityRewardStatus.OBTAINED)
                {
                    ShowTip("该VIP礼包今日已经购买");
                    return;
                }
                if (status != ActivityRewardStatus.FINISHED)
                {
                    ShowTip("VIP等级尚未达到购买条件");
                    return;
                }

                VipPrizeActivityData data = GetVipPrizeData(pid);
                PlayerDataStruct player = GetPlayer();
                if (data == null || player == null)
                {
                    ShowTip("礼包配置不存在");
                    return;
                }
                if (player.GetCurrency(CoinType.YuanBao) <
                    data.currentCost)
                {
                    ShowTip("元宝不足");
                    return;
                }

                player.AddCurrency(-data.currentCost, CoinType.YuanBao);
                MessageEventArgs message = new MessageEventArgs();
                if (!GrantItems(
                        data.itemIDList,
                        data.itemRuleList,
                        message))
                {
                    player.AddCurrency(
                        data.currentCost,
                        CoinType.YuanBao);
                    ShowTip("礼包发放失败，元宝已退回");
                    return;
                }

                SetClaimed(VipPrizeClaimPrefix, pid);
                message.AddMessage(
                    "VIPPrizeActivityRewardObtained",
                    string.Empty);
                YuXueUIUtils.OpenNewItemWindowFromGiftMessage(message);
                EventManager.Instance.PostEvent(
                    (EventDefine)RewardObtainedEvent,
                    message);
                LocalProgression.SavePlayerData();
                ShowTip("VIP礼包购买成功");
                Log("Buy VIP prize pid=" + pid);
            }
            catch (Exception ex)
            {
                ShowTip("购买VIP礼包时出现异常");
                Log("Buy VIP prize ERR " + ex);
            }
        }

        public static int GetWindmillRemainCount(
            WindmillActivityContent content)
        {
            ActivityRewards.EnsureDailyState();
            int max = content == null
                ? 0
                : content.maxWindTimes;
            return Math.Max(0, max - GetWindmillIndex());
        }

        public static void PrepareWindmill(
            WindmillActivityContent content)
        {
            ActivityRewards.EnsureDailyState();
            if (content == null)
            {
                return;
            }

            FieldInfo indexField = GetWindmillIndexField();
            if (ReferenceEquals(indexField, null))
            {
                return;
            }
            int index = Math.Min(
                GetWindmillIndex(),
                Math.Max(0, content.maxWindTimes));
            indexField.SetValue(content, index);
        }

        public static void RequestWindmill(
            WindmillActivityContent content)
        {
            try
            {
                ActivityRewards.EnsureDailyState();
                if (content == null ||
                    content.remainCount <= 0)
                {
                    ShowTip("今日转盘次数已经用完");
                    return;
                }

                WindmillActivityData data = content.currRewardData;
                int rewardIndex = SelectWeightedIndex(data);
                if (data == null ||
                    rewardIndex < 0 ||
                    rewardIndex >= data.rewardList.Count)
                {
                    ShowTip("转盘奖励配置异常");
                    return;
                }

                MessageEventArgs message = new MessageEventArgs();
                message.AddMessage(
                    "WindmillActivityRewardObtained",
                    string.Empty);
                message.AddMessage(
                    "rewardYuanbaoIndex",
                    rewardIndex.ToString());
                message.AddMessage(
                    "rewardYuanbao",
                    data.rewardList[rewardIndex].ToString());
                EventManager.Instance.PostEvent(
                    (EventDefine)RewardObtainedEvent,
                    message);
                Log("Windmill request rewardIndex=" + rewardIndex);
            }
            catch (Exception ex)
            {
                ShowTip("转盘请求出现异常");
                Log("Windmill request ERR " + ex);
            }
        }

        public static void CommitWindmillStep(
            WindmillActivityContent content)
        {
            if (content == null)
            {
                return;
            }

            FieldInfo indexField = GetWindmillIndexField();
            if (ReferenceEquals(indexField, null))
            {
                return;
            }
            int index = Math.Max(
                0,
                Math.Min(
                    content.maxWindTimes,
                    (int)indexField.GetValue(content)));
            PlayerPrefs.SetInt(GetDatedKey(WindmillIndexPrefix), index);
            PlayerPrefs.Save();
            LocalProgression.SavePlayerData();
            Log("Windmill committed index=" + index);
        }

        public static void NormalizeServerFirstChargePanel(
            UIServerFirstChargeContentPanel panel)
        {
            if (panel == null)
            {
                return;
            }

            if (panel.activityDescLabel != null)
            {
                panel.activityDescLabel.text = ReplaceCountWithAmount(
                    panel.activityDescLabel.text);
            }

            UILabel[] labels =
                panel.GetComponentsInChildren<UILabel>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                UILabel label = labels[i];
                if (label == null || string.IsNullOrEmpty(label.text))
                {
                    continue;
                }
                label.text = ReplaceCountWithAmount(label.text);
            }
        }

        private static string ReplaceCountWithAmount(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            return text.Replace("首充人数", "首充金额")
                       .Replace("充值人数", "充值金额")
                       .Replace("完成人数", "完成金额");
        }

        private static int SelectWeightedIndex(
            WindmillActivityData data)
        {
            if (data == null ||
                data.rewardList == null ||
                data.rewardList.Count == 0)
            {
                return -1;
            }

            int totalWeight = 0;
            if (data.weightList != null)
            {
                for (int i = 0; i < data.weightList.Count; i++)
                {
                    if (data.weightList[i] > 0)
                    {
                        totalWeight += data.weightList[i];
                    }
                }
            }
            if (totalWeight <= 0)
            {
                return UnityEngine.Random.Range(0, data.rewardList.Count);
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            int cumulative = 0;
            for (int i = 0; i < data.rewardList.Count; i++)
            {
                int weight = data.weightList != null &&
                             i < data.weightList.Count
                    ? data.weightList[i]
                    : 0;
                if (weight <= 0)
                {
                    continue;
                }
                cumulative += weight;
                if (roll < cumulative)
                {
                    return i;
                }
            }
            return data.rewardList.Count - 1;
        }

        private static int GetWindmillIndex()
        {
            return Math.Max(
                0,
                PlayerPrefs.GetInt(GetDatedKey(WindmillIndexPrefix), 0));
        }

        private static FieldInfo GetWindmillIndexField()
        {
            return typeof(WindmillActivityContent).GetField(
                "rewardIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static ServerFirstChargeActivityData
            GetServerFirstChargeData(int pid)
        {
            try
            {
                return DataManager.Instance
                    .newServerActivityMetaDatas
                    .GetServerFirstChargeActivityDataByPID(pid);
            }
            catch
            {
                return null;
            }
        }

        private static VipPrizeActivityData GetVipPrizeData(int pid)
        {
            try
            {
                return DataManager.Instance
                    .newServerActivityMetaDatas
                    .GetVIPPrizeActivityDataByPID(pid);
            }
            catch
            {
                return null;
            }
        }

        private static PlayerDataStruct GetPlayer()
        {
            return DataManager.Instance == null
                ? null
                : DataManager.Instance.playerDataStruct;
        }

        private static int GetVipLevel()
        {
            PlayerDataStruct player = GetPlayer();
            return player == null ? 0 : player.VLevel;
        }

        private static bool GrantItems(
            List<int> itemIds,
            List<int> rules,
            MessageEventArgs message)
        {
            if (itemIds == null)
            {
                return false;
            }

            int currencyCount = 0;
            InitializeRewardMessage(message);

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
                bool isCurrency = itemId / 100000 == 5;
                int amount = isCurrency
                    ? GetCurrencyRewardAmount(itemId, rule)
                    : LocalGameplay.GetRewardAmount(itemId, rule);
                if (amount <= 0)
                {
                    amount = 1;
                }
                Log("Reward amount item=" + itemId +
                    " rule=" + rule +
                    " amount=" + amount +
                    " currency=" + isCurrency);

                if (isCurrency)
                {
                    Currency currency = ItemManager.Instance
                        .GenerateCurrencyByAmount(itemId, amount);
                    if (currency == null)
                    {
                        Log("Grant currency failed id=" + itemId +
                            " amount=" + amount);
                        return false;
                    }
                    message.AddMessageReplace(
                        "currencyType" + currencyCount,
                        currency.prototypeID.ToString());
                    message.AddMessageReplace(
                        "currencyNum" + currencyCount,
                        amount.ToString());
                    currencyCount++;
                    continue;
                }

                MessageEventArgs itemMessage = new MessageEventArgs();
                if (!LocalGameplay.GrantSignInItemDirect(
                        itemId,
                        amount,
                        rule,
                        itemMessage))
                {
                    Log("Grant item failed id=" + itemId +
                        " rule=" + rule);
                    return false;
                }
                AppendRewardMessage(message, itemMessage);
            }
            message.AddMessageReplace(
                "currencyCount",
                currencyCount.ToString());
            return true;
        }

        private static int GetCurrencyRewardAmount(
            int itemId,
            int ruleId)
        {
            try
            {
                Item item = ItemManager.Instance
                    .GetItemPrototype(itemId);
                GenerateRule rule =
                    ItemManager.Instance.GetGenerateRule(ruleId);
                if (item == null || rule == null)
                {
                    return 1;
                }

                if (Convert.ToBoolean(
                        rule.GetValue("RandomCount")))
                {
                    int minimum = ReadInt(
                        rule.GetValue("MinCount"));
                    int maximum = ReadInt(
                        rule.GetValue("MaxCount"));
                    if (maximum < minimum)
                    {
                        maximum = minimum;
                    }
                    return MathUtility.GetRandomInt(
                        ruleId,
                        minimum,
                        maximum + 1);
                }

                return Math.Max(
                    1,
                    ReadInt(rule.GetValue("Count")));
            }
            catch (Exception ex)
            {
                Log("Currency amount ERR item=" + itemId +
                    " rule=" + ruleId + " " + ex);
                return 1;
            }
        }

        private static void InitializeRewardMessage(
            MessageEventArgs message)
        {
            message.AddMessageReplace("currencyCount", "0");
            message.AddMessageReplace("EquipCount", "0");
            message.AddMessageReplace("RuneCount", "0");
            message.AddMessageReplace("GemCount", "0");
            message.AddMessageReplace("MatCount", "0");
            message.AddMessageReplace("StackCount", "0");
        }

        private static void AppendRewardMessage(
            MessageEventArgs target,
            MessageEventArgs source)
        {
            AppendCategory(
                target,
                source,
                "EquipCount",
                "EquipUID",
                null);
            AppendCategory(
                target,
                source,
                "RuneCount",
                "RuneID",
                null);
            AppendCategory(
                target,
                source,
                "GemCount",
                "GemID",
                null);
            AppendCategory(
                target,
                source,
                "MatCount",
                "MatID",
                null);
            AppendCategory(
                target,
                source,
                "StackCount",
                "StackID",
                "StackCount");
        }

        private static void AppendCategory(
            MessageEventArgs target,
            MessageEventArgs source,
            string countKey,
            string idPrefix,
            string amountPrefix)
        {
            int sourceCount = ReadInt(source.GetMessage(countKey));
            if (sourceCount <= 0)
            {
                return;
            }

            int targetOffset = ReadInt(target.GetMessage(countKey));
            for (int i = 0; i < sourceCount; i++)
            {
                string sourceId = source.GetMessage(idPrefix + i);
                if (string.IsNullOrEmpty(sourceId))
                {
                    continue;
                }
                target.AddMessageReplace(
                    idPrefix + (targetOffset + i),
                    sourceId);
                if (!string.IsNullOrEmpty(amountPrefix))
                {
                    string sourceAmount =
                        source.GetMessage(amountPrefix + i);
                    if (!string.IsNullOrEmpty(sourceAmount))
                    {
                        target.AddMessageReplace(
                            amountPrefix + (targetOffset + i),
                            sourceAmount);
                    }
                }
            }
            target.AddMessageReplace(
                countKey,
                (targetOffset + sourceCount).ToString());
        }

        private static int ReadInt(string value)
        {
            int result;
            return int.TryParse(value, out result) ? result : 0;
        }

        private static bool IsClaimed(string prefix, int pid)
        {
            int mask = PlayerPrefs.GetInt(GetDatedKey(prefix), 0);
            return (mask & (1 << (pid - 1))) != 0;
        }

        private static void SetClaimed(string prefix, int pid)
        {
            string key = GetDatedKey(prefix);
            int mask = PlayerPrefs.GetInt(key, 0);
            mask |= 1 << (pid - 1);
            PlayerPrefs.SetInt(key, mask);
            PlayerPrefs.Save();
        }

        private static string GetDatedKey(string prefix)
        {
            return prefix + DateTime.Now.ToString("yyyyMMdd");
        }

        private static void ShowTip(string text)
        {
            YuXueUIUtils.ShowTips(text);
        }

        private static void Log(string text)
        {
            Debug.Log("[YzrLocal.NewServer] " + text);
        }
    }
}
