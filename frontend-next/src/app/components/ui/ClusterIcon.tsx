'use client';

import React from 'react';
import AppsRoundedIcon from '@mui/icons-material/AppsRounded';
import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded';
import BadgeRoundedIcon from '@mui/icons-material/BadgeRounded';
import Inventory2RoundedIcon from '@mui/icons-material/Inventory2Rounded';
import AssignmentRoundedIcon from '@mui/icons-material/AssignmentRounded';
import DevicesRoundedIcon from '@mui/icons-material/DevicesRounded';
import SwapHorizRoundedIcon from '@mui/icons-material/SwapHorizRounded';
import SupportAgentRoundedIcon from '@mui/icons-material/SupportAgentRounded';
import EventAvailableRoundedIcon from '@mui/icons-material/EventAvailableRounded';
import PaymentsRoundedIcon from '@mui/icons-material/PaymentsRounded';
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded';
import StorageRoundedIcon from '@mui/icons-material/StorageRounded';
import LayersRoundedIcon from '@mui/icons-material/LayersRounded';
import HandshakeRoundedIcon from '@mui/icons-material/HandshakeRounded';
import EngineeringRoundedIcon from '@mui/icons-material/EngineeringRounded';
import BusinessCenterRoundedIcon from '@mui/icons-material/BusinessCenterRounded';
import StorefrontRoundedIcon from '@mui/icons-material/StorefrontRounded';
import LocalShippingRoundedIcon from '@mui/icons-material/LocalShippingRounded';

export interface ClusterIconProps {
  icon?: string | null;
  fontSize?: number;
  className?: string;
  style?: React.CSSProperties;
  color?: string;
}

const ICON_MAP: Record<string, React.ElementType> = {
  apps: AppsRoundedIcon,
  groups: GroupsRoundedIcon,
  badge: BadgeRoundedIcon,
  inventory: Inventory2RoundedIcon,
  assignment: AssignmentRoundedIcon,
  devices: DevicesRoundedIcon,
  swaphoriz: SwapHorizRoundedIcon,
  supportagent: SupportAgentRoundedIcon,
  eventavailable: EventAvailableRoundedIcon,
  payments: PaymentsRoundedIcon,
  factcheck: FactCheckRoundedIcon,
  storage: StorageRoundedIcon,
  layers: LayersRoundedIcon,
  handshake: HandshakeRoundedIcon,
  engineering: EngineeringRoundedIcon,
  businesscenter: BusinessCenterRoundedIcon,
  storefront: StorefrontRoundedIcon,
  localshipping: LocalShippingRoundedIcon,
};

export const APP_ICON_OPTIONS = [
  { value: 'Groups', label: 'Nhân sự / Đội ngũ', icon: GroupsRoundedIcon },
  { value: 'Inventory', label: 'Tài sản / Kho vận', icon: Inventory2RoundedIcon },
  { value: 'Assignment', label: 'Yêu cầu / Công việc', icon: AssignmentRoundedIcon },
  { value: 'Payments', label: 'Tài chính / Kế toán', icon: PaymentsRoundedIcon },
  { value: 'Handshake', label: 'Khách hàng / CRM', icon: HandshakeRoundedIcon },
  { value: 'Storefront', label: 'Bán hàng / POS', icon: StorefrontRoundedIcon },
  { value: 'LocalShipping', label: 'Chuỗi cung ứng / Giao nhận', icon: LocalShippingRoundedIcon },
  { value: 'Engineering', label: 'Kỹ thuật / Dự án', icon: EngineeringRoundedIcon },
  { value: 'BusinessCenter', label: 'Điều hành / Quản trị', icon: BusinessCenterRoundedIcon },
  { value: 'Devices', label: 'Thiết bị / CNTT', icon: DevicesRoundedIcon },
  { value: 'Badge', label: 'Hồ sơ / Định danh', icon: BadgeRoundedIcon },
  { value: 'FactCheck', label: 'Kiểm duyệt / Phê duyệt', icon: FactCheckRoundedIcon },
  { value: 'Storage', label: 'Dữ liệu / Danh mục', icon: StorageRoundedIcon },
  { value: 'Layers', label: 'Phân hệ chung', icon: LayersRoundedIcon },
];

export default function ClusterIcon({
  icon,
  fontSize = 18,
  className = '',
  style,
  color,
}: ClusterIconProps) {
  const key = (icon || 'layers').trim().toLowerCase().replace(/[^a-z0-9]/g, '');
  const IconComp = ICON_MAP[key] || LayersRoundedIcon;
  return <IconComp className={className} style={{ fontSize, color, ...style }} />;
}
